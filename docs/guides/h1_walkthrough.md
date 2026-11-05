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
   - **Sub-decision — LOCKED (2026-11-04):** option (a). Redis
     *value* = the original `eventId` (`SET dedup:key <guid>`).
     Redis hit → answer `duplicate` straight from Redis, no PG
     roundtrip, no `ContentHash` column.
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
- **Redis hit → the value IS the eventId.** With (a) there is no
  PG lookup on the hit path — `duplicate` comes straight from the
  stored guid. Degradation to know: Redis says dup but the PG row
  is gone (manual wipe) → client gets a dangling id. Accepted:
  cache-ahead-of-truth only under external deletion. Also: the
  `duplicate` response on this path has `eventId` + `status`, no
  `eventStatus` field — the PG-catch path still returns the full
  row shape. Both shapes are fine; note it in the journal.
- **Unit-mismatch #2 (fractional window).** `WindowHours` becomes a
  `double` so a sub-hour window is possible (e.g. `0.002` ≈ 7s —
  this is how you verify the bucket boundary in seconds, not by
  waiting an hour). Trap: `windowHours * 3600 < 1` →
  `(long)(0.36) = 0` → **division by zero** in the bucket math.
  Guard the *seconds*, not the hours.
- **Old-format Redis values.** Existing keys hold `"1"`, not a
  guid. A guid-parse failure → treat as miss → falls through to
  insert → PG index decides. Self-healing, no migration needed.
- **TTL anchored at first sight** — Redis `SET NX EX` does NOT slide:
  each dup hit does not extend the window. Dedup horizon = first
  event + window, not last dup + window. Know which you implemented.

## Write map — every edit, in order

Interface first (signature is the contract, then implementations):

**`Core/Abstractions/IDeduplicationStore.cs`** — the pair becomes:
```csharp
Task<Guid?> GetEventIdAsync(string key, CancellationToken ct = default);
Task MarkAsync(string key, Guid eventId, TimeSpan window, CancellationToken ct = default);
```
`IsKnownAsync` is gone — a non-null guid IS "known". Only caller:
`IngestEvent`.

**`Infrastructure/Redis/RedisDeduplicationStore.cs`**
- `GetEventIdAsync`: `StringGetAsync(Prefixed(key))` →
  `Guid.TryParse` → guid or null (old `"1"` values → null → miss).
  Same `catch (RedisException)` → null (fail-open) as before.
- `MarkAsync`: `StringSetAsync(Prefixed(key), eventId.ToString(), window, When.NotExists)`.

**`Api/Endpoints/Endpoints.cs`** — `IngestEvent`:
1. Signature: add `TimeProvider time` (before `CancellationToken`).
   DI already has `TimeProvider.System`.
2. Replace `var key = ComputeKey(...)` (line ~81) with the
   window/bucket computation + both keys:
   `windowHours` (double, `<= 0 → 1`) → `windowSecs = Math.Max(1, …)`
   → `bucket = time.GetUtcNow().ToUnixTimeSeconds() / windowSecs`
   → `redisKey = ComputeKey(wh, payload)` → `key = ComputeKey(wh, payload, bucket)`.
3. Dedup check: `dedup.GetEventIdAsync(redisKey, ct)` → `Guid` →
   `metrics.DedupHit()` + return `{ eventId, "duplicate" }`. The PG
   `FirstOrDefaultAsync` fallback inside the Redis-hit branch is
   deleted — the value carries the id now.
4. Line ~106: `DateTimeOffset.UtcNow` → `time.GetUtcNow()` (same
   clock as the bucket).
5. 23505-catch: **unchanged** — `key` is already the bucketed pgKey.
6. Line ~155: `window = TimeSpan.FromHours(windowHours)`;
   `MarkAsync(redisKey, ev.Id, window)` — note: **redisKey**, not
   `key`. Marking the bucketed key would dedup forever on the
   content the rolling window was supposed to expire.

**`ComputeKey`** — signature `(Guid webhookId, string payload,
long? bucket = null)`; `null` → `"{wh}:{payload}"`, else
`"{wh}:{payload}:{bucket}"`. Same SHA256 hex-lower.

## Tests / verification

`ComputeKey` is `private` in Api — the test project doesn't
reference it. Options: (a) move it to `Core` (pure helper, say
`DedupKey`) — one-line move, then unit tests are free; (b) skip
unit tests, verify end-to-end only. If (a): say the word, the move
is mechanical and it's my side of the fence.

End-to-end (the real AC): run with `Dedup__WindowHours=0.002`
(≈7s bucket) → POST → POST → `duplicate`; sleep ~8s → POST →
**new event id, second PG row**. Reset to 24 after.

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
