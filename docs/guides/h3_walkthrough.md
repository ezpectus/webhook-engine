# H-3 walkthrough — lease-version claim (fix for chaos #7)

Hands-on task. This doc lays out the path; the implementation is
yours. Ticket: `docs/tasks.md` → H-3. Independent of H-1/H-2 — can
be done in any order.

## What you're building

Chaos #7: a slow send outlives the 5-min InFlight lease → sweeper
reclaims → second worker sends in parallel (legal, at-least-once)
→ **both** workers then `SaveChanges`: `AttemptCount++` is
last-write-wins, attempt rows can double-number, status flip-flops.
The send-dup stays (unavoidable); the *write* must become
conditional.

`ClaimVersion int` is already on `notification_events` (migration
`AddEventClaimVersion` applied — nothing to scaffold).

Mechanism: every lease transfer bumps the version; the worker's
result-write only lands `WHERE ClaimVersion = <version it saw>`.
Mismatch → its lease died mid-flight → drop the whole write.

## Where the code lives

Lease transitions to guard (each is an `ExecuteUpdateAsync` — add
`.SetProperty(e => e.ClaimVersion, e => e.ClaimVersion + 1)`):

| Site | File | Lines | Transition |
|------|------|-------|-----------|
| ingest claim | `Endpoints.cs` | ~134–138 | Pending→InFlight |
| channel-full rollback | `Endpoints.cs` | ~143–148 | InFlight→Pending |
| replay claim | `Endpoints.cs` | ~199 | DeadLettered→InFlight (tracked entity — `ev.ClaimVersion++`, not SetProperty) |
| stale reclaim | `SweeperService.cs` | ~49–53 | InFlight→Pending |
| sweeper claim | `SweeperService.cs` | ~83–88 | Pending/RetryScheduled→InFlight |
| channel-full rollback | `SweeperService.cs` | ~96–100 | InFlight→Pending |

The write to guard:

| Site | File | Lines |
|------|------|-------|
| limiter-defer save | `DeliveryWorker.cs` | ~82 (`SaveChangesAsync`) |
| result save | `DeliveryWorker.cs` | ~149 (`SaveChangesAsync`) |

## Decisions — pick per line, defaults recommended

1. **Bump on every lease transfer — recommended.** Rule:
   *"whoever takes or seizes the lease bumps it."* Claims AND the
   stale-reclaim. The reclaim-bump kills a stale worker's write the
   moment the sweeper touches the row — even in the gap before
   re-claim. Alternative (claim-only) leaves a small benign window
   (stale worker can flip Pending→Delivered between reclaim and
   re-claim — outcome usually correct anyway, but harder to reason
   about). Owner rollbacks (channel-full): no foreign writer
   exists → bump optional; skipping is fine, say which you chose.
2. **`ClaimVersion` as EF concurrency token — recommended.** In
   `WebhookEngineDbContext` OnModelCreating:
   `e.Property(x => x.ClaimVersion).IsConcurrencyToken()`. EF then
   appends `AND "ClaimVersion" = @original` to the UPDATE and
   throws `DbUpdateConcurrencyException` on 0 rows — covering
   *both* SaveChanges and the `Attempts.Add` insert atomically
   (one tx). Alternative `ExecuteUpdateAsync + rows==1` splits the
   guard from the attempt-insert → needs an explicit tx, two
   failure modes. More code for less safety.
3. **Catch shape:** wrap `ProcessAsync` body in
   `try / catch (DbUpdateConcurrencyException)` →
   `logger.LogInformation` (expected under lease loss, not an
   error) → return. The `Attempts.Add` rolls back with the event
   update — no half-written result, no feed publish (the status
   never happened).
4. **Version source = the tracked entity.** `tracked` is loaded
   via `FindAsync` *after* the claim → its `ClaimVersion` IS the
   lease the worker holds. No signature changes, nothing rides in
   the channel item.

## Write map — every edit, in order

1. `WebhookEngineDbContext`: `.IsConcurrencyToken()` on
   `ClaimVersion` (next to the existing `IdempotencyKey` index
   config).
2. The four `ExecuteUpdateAsync` sites: `+ .SetProperty(e =>
   e.ClaimVersion, e => e.ClaimVersion + 1)`. (Ingest claim,
   sweeper claim, sweeper reclaim — plus rollback sites if you
   chose to bump there.)
3. `ReplayDeadLetter`: tracked-entity path → `ev.ClaimVersion++`
   next to `ev.Status = InFlight`.
4. `DeliveryWorker.ProcessAsync`: wrap the body in
   `try { … } catch (DbUpdateConcurrencyException) { log + return }`.
   Both `SaveChangesAsync` calls are inside — one guard covers
   limiter-defer AND result-write.
5. Nothing else — the stale-channel-item filter (`Status !=
   InFlight`) stays; the version guard is *under* it, catching the
   race it can't see (status matches, lease doesn't).

## Edge cases to think about

- **Parallel send still happens** — version guards the WRITE, not
  the wire. Both workers may fire HTTP; at-least-once holds. The
  fix makes `AttemptCount`/attempts *consistent*, not the send
  once. Say this at the checkpoint.
- **Reclaim without re-claim:** worker reads `tracked` (v1),
  sweeper reclaims (v2), worker writes `WHERE v=1` → dropped —
  even though status is still Pending. The version saw what the
  status-filter couldn't.
- **Two reclaims during one send** (tiny lease): v1→v2→v3 — every
  stale writer drops; only the current holder's save lands. The
  repro below does exactly this.
- **`DbUpdateConcurrencyException` ≠ bug** — it's the mechanism
  working. Information-level log, no alert.
- **Worker crash between guarded update and attempt insert:** can't
  happen — single `SaveChanges` is one tx. (This is why the token
  beats the rows-check split.)

## Verify — chaos #7, fast version

Two config knobs make it seconds instead of 5 minutes. Both read
as `double` (one-line change each — same fractional trick as H-1's
`WindowHours`):

- `Sweeper:InFlightTimeoutMinutes=0.05` (3s lease)
- `Sweeper:IntervalSeconds=1`

Then: webhook → `mockreceiver:5005/hook?mode=slow` (15s delay vs
sender's ~10s timeout), POST one event, watch:

```
t0   worker A claims v1, send starts
t3   sweeper reclaims → Pending v2 → re-claim → v3, worker B sends
t6   another reclaim cycle (v4) …
t10  A's send times out → A writes WHERE v=1 → 0 rows → dropped
t~15 B finishes → writes WHERE v=current → lands once
```

PG check after: `AttemptCount` matches `count(*)` in
`delivery_attempts` for that event, status terminal-consistent.
Pre-fix you'd see the lost increment or doubled attempt number.

## Definition of done

- [ ] `.IsConcurrencyToken()` on `ClaimVersion`; bumps on the
      lease transfers you chose (journal: which + why)
- [ ] `DbUpdateConcurrencyException` → drop + info log (no feed
      publish of a status that never persisted)
- [ ] Repro above shows: send-dup may happen (legal), counters and
      attempt rows stay consistent
- [ ] Journal line in `PLAN.md`: why the version guards the write
      and not the send; what the stale status-filter can't see
- [ ] Voice-checkpoint: trace reclaim → re-claim → stale write;
      name the exact `WHERE` clause that saves the row
