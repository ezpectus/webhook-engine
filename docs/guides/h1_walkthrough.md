# H-1 walkthrough — honest dedup window (time bucket in the key)

Hands-on task. This doc lays out the path; the implementation is
yours. No code below. Ticket: `docs/tasks.md` → H-1.

## What you're building

Today `IdempotencyKey = sha256(webhookId + ":" + payload)` — one key
**forever**. The PG unique index therefore dedups "same POST next
week" as a duplicate, even though semantically that's a new event
(chaos run #9 proved it: `Dedup:WindowHours=0` still returned
`duplicate`).

Your change makes the window **honest**: same payload inside the
window → duplicate; same payload in the next window → a new event.

The mechanism: add a *time bucket* to the key.

```
bucket = now.ToUnixTimeSeconds() / (WindowHours * 3600)   // integer division
key    = sha256(webhookId + ":" + payload + ":" + bucket)
```

Watch the units: `now` in **seconds**, window converted to seconds too.
`now / WindowHours` (seconds / hours) is the classic unit-mismatch bug.

## Where the code lives

- `src/WebhookEngine.Api/Endpoints/Endpoints.cs`
  - `IngestEvent` — computes `key` at the top of the handler (~line 74)
  - `ComputeKey(webhookId, payload)` — the private helper at the bottom
- `appsettings.json` → `Dedup:WindowHours` (already exists, =24)
- Redis side: `MarkAsync(key, window)` — no change needed; the bucket
  is already inside `key`, the TTL stays the same window length

## Decisions BEFORE you write (the mini-plan, say it out loud)

1. **Where does `now` come from?** Endpoints are static — you can't
   add a ctor field. Options: inject `TimeProvider` as a handler
   parameter (`IngestEvent(..., TimeProvider time, ...)`) or call
   `DateTimeOffset.UtcNow` inside. The injection route is the
   testable one — and it's the point of this exercise.
   *Note: `TimeProvider` may not resolve in DI by default — if not,
   `services.AddSingleton(TimeProvider.System)` in `Program.cs` is
   plumbing; ping Devin or add it yourself.*
2. **Fixed vs rolling window.** `floor(now / window)` gives *fixed*
   buckets. Consequence you must be able to defend: a POST at
   bucket-edge 23:59 and the identical POST at 00:01 land in
   **different** buckets → accepted twice, 2 minutes apart. That's
   the documented trade-off of fixed windows (every system with
   time-bucketed idempotency has it). Rolling windows don't have the
   edge — but cost a store lookup per request. Say which you picked
   and why.
3. **`WindowHours = 0` or negative.** Division by zero / negative
   bucket — decide: treat `<= 0` as "bucket disabled" (old permanent
   behavior) or reject the config. One line, but it's a real branch.
4. **Does the bucket belong inside `ComputeKey`?** Passing `bucket`
   as a parameter keeps `ComputeKey` a pure function — easier to
   unit-test than reaching for config inside it.

## Edge cases to think about

- **Boundary straddle** — same payload, adjacent buckets → two events.
  Intended, but your tests should *prove* it, not just your mouth.
- **Dedup response after the boundary** — a late client retry gets a
  new `eventId` + 202, not `duplicate` + old id. Is that what a
  retrying client wants? (This is exactly why H-2 — a real
  client-supplied Idempotency-Key — exists.)
- **`23505` catch still works** — the bucket is inside the same
  unique-indexed string; nothing to change there.
- **Redis TTL vs bucket drift** — a key marked near window end could
  outlive its bucket's usefulness slightly. Harmless (PG index is
  truth), but be able to say why.

## Tests you write

- `ComputeKey` (or whatever you factor the bucket into): same input +
  same bucket → identical key; same input + adjacent bucket →
  different keys. Pure, fast, no infra.
- If you inject `TimeProvider`/`now`: `IngestEvent` with a fake clock —
  same payload at t and at t+window → two events, two ids; within the
  same bucket → `duplicate`, one row.
- `WindowHours <= 0` branch — whichever behavior you chose.

## Definition of done

- [ ] AC from the ticket: same payload in the *next* window → new
      event; inside the window → `duplicate`
- [ ] Your mini-plan spoken/written BEFORE the code (flow → where's
      the dup → where's the loss → why OK)
- [ ] Journal line in `PLAN.md`: bucket semantics + the
      boundary-trade-off you accepted
- [ ] `dotnet test` green
- [ ] Voice-checkpoint: explain why a boundary-straddling POST is an
      acceptable dup — and why the *pre-bucket* permanent dedup was
      the actual bug
