 H-1 walkthrough — honest dedup window (time bucket in the key)

Hands-on task. This doc lays out the path; the implementation is
yours. No code below. Ticket: `docs/tasks.md` → H-1.

## What you're building

Today `IdempotencyKey = sha256(webhookId + ":" + payload)` — one key
**forever**. The PG unique index therefore dedups "same POST next
week" as a duplicate, even though semantically that's a new event
(chaos run #9 proved it: `Dedup:WindowHours=0` still returned
`duplicate`).

Your change makes the window **honest**: same payload inside the
window → duplicate; same payload after the window → a new event.

The mechanism: a *time bucket* in the PG key + the Redis TTL as the
rolling horizon (see the locked decision below).

```
bucket   = now.ToUnixTimeSeconds() / (WindowHours * 3600)   // integer division
pgKey    = sha256(webhookId + ":" + payload + ":" + bucket)
redisKey = sha256(webhookId + ":" + payload)                 // unchanged — rolling via TTL
```

Watch the units: `now` in **seconds**, window converted to seconds too.
`now / WindowHours` (seconds / hours) is the classic unit-mismatch bug.

## Where the code lives

- `src/WebhookEngine.Api/Endpoints/Endpoints.cs`
  - `IngestEvent` — computes `key` at the top of the handler (~line 74),
    `DateTimeOffset.UtcNow` at ~line 106, `MarkAsync` at ~line 156
  - `ComputeKey(webhookId, payload)` — the private helper at the bottom
- `src/WebhookEngine.Infrastructure/Redis/RedisDeduplicationStore.cs`
  - `MarkAsync(key, "1", window)` — if you pick the value-in-Redis
    option, the stored value becomes the `eventId`
- `appsettings.json` → `Dedup:WindowHours` (already exists, =24)

## Decisions — LOCKED (dictation session 2026-10-28)

1. **`now` → injected `TimeProvider`** as a handler parameter on
   `IngestEvent`. Prod-standard since .NET 8 (testable time;
   `FakeTimeProvider` in tests). `TimeProvider.System` is already in
   DI.
2. **Rolling window — via a split design.** PG's unique index
   *cannot* express "unique only within last N hours" (`now()` is
   not immutable in index predicates). Rolling lives where it's
   free: Redis `SET NX EX window` expires `window` after first
   sight = a rolling window anchored at first-seen.
   ```
   Redis key = sha256(wh + payload)          → rolling dedup (fast path)
   PG    key = sha256(wh + payload + bucket) → fixed-bucket backstop
   ```
   - Boundary straddle closed while Redis is up (its key doesn't
     know about buckets); possible only during Redis outage via the
     PG backstop — a rare, documented degradation.
   - Two keys are needed: one for `IsKnownAsync`/`MarkAsync`
     (content key), one stored in `IdempotencyKey` (bucketed key).
   - **Open sub-decision (pick when writing):** on a Redis hit you
     need the *original* `eventId` — can't find the PG row by
     `IdempotencyKey` (it's bucketed). Either (a) store `eventId`
     as the Redis *value* (`SET dedup:key <guid>` — small
     `IDeduplicationStore` shape change, no extra index) or (b) add
     a non-unique `ContentHash` column and look up by it.
3. **`WindowHours <= 0` → clamp to 1.** Dedup never divides by
   zero and never silently disables. (Alternatives considered:
   "0 = forever" and fail-fast-on-config — both defensible; clamp
   chosen as simplest safe default.)
4. **`bucket` is a parameter of `ComputeKey`.** Pure function,
   unit-testable without infra. Signature consequence of the split
   design: `ComputeKey(Guid webhookId, string payload, long? bucket = null)`
   — `null` yields the content key for Redis, a value yields the
   bucketed key for PG.

## Edge cases to think about

- **Boundary straddle** — closed while Redis is up (content key has
  no bucket). Open only when Redis is down AND the post straddles a
  bucket edge: PG's bucketed index sees two different keys → two
  events. Acceptable dup — but be able to name the exact condition.
- **Dedup response after the window** — a late client retry gets a
  new `eventId` + 202, not `duplicate` + old id. Is that what a
  retrying client wants? (This is exactly why H-2 — a real
  client-supplied Idempotency-Key — exists.)
- **`23505` catch still works** — the bucket is inside the same
  unique-indexed string; nothing to change there.
- **Redis hit → find the original event.** `IdempotencyKey` is
  bucketed, so `WHERE IdempotencyKey = contentKey` finds nothing —
  this is the open sub-decision (Redis value = eventId vs
  `ContentHash` column). Edge: Redis says dup but the PG row is
  gone — fall through to insert, same as today.
- **TTL anchored at first sight** — Redis `SET NX EX` does NOT slide:
  each dup hit does not extend the window. Dedup horizon = first
  event + window, not last dup + window. Know which you implemented.

## Tests you write

- `ComputeKey`: same input + same bucket → identical key; same input +
  adjacent bucket → different keys; `bucket: null` → stable content
  key for any time. Pure, fast, no infra.
- `IngestEvent` with a fake clock: same payload at t and at t+window →
  two events, two ids; within the window → `duplicate`, one row.
- `WindowHours <= 0` branch — the clamp: config `0` behaves like `1`.

## Definition of done

- [ ] AC from the ticket: same payload in the *next* window → new
      event; inside the window → `duplicate`
- [ ] Your mini-plan spoken/written BEFORE the code (flow → where's
      the dup → where's the loss → why OK)
- [ ] Journal line in `PLAN.md`: bucket semantics + the
      boundary-trade-off you accepted
- [ ] `dotnet test` green
- [ ] Voice-checkpoint: name the ONE remaining straddle-dup case
      (Redis down + bucket edge) and why it's acceptable — and why
      the *pre-bucket* permanent dedup was the actual bug
