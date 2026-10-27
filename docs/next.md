# What's next — the queue

As of 2026-10-26. The full delivery loop is implemented, wired, and
**verified live on the containerized stack** — 9 chaos failures
reproduced, ~6000 events through LoadGen, zero loss after accept.
Order below is by dependencies, not importance.

## Blocking now

1. **User's final story** — "where it can dup / where it can lose,
   and why that's OK/not OK" against the boundary map in `PLAN.md`.
   That's the project's acceptance criterion.
2. **TASK-011 remainder** — core docs are EN; working artifacts
   (tasks/done-log/open_questions/CHANGELOG) stay RU by design —
   they're the process log, not the portfolio face.

## Next wave (optional hands-on — the user's practice lane)

3. **Q8 fix — dedup window bucketing**: `sha256(webhook + payload +
   floor(now/24h))` makes the dedup horizon honest. ~20 lines in
   `IngestEvent` — perfect first hands-on session.
4. **Client `Idempotency-Key` header** — Stripe-style: client key wins,
   content-hash as fallback. Evolution of D — real portfolio feature.
5. **Q7 lease-versioning** — version column on the claim; a worker may
   write only under its own version. Kills the dirty `AttemptCount`
   boundary. The most "distributed" remaining task.

## Scale wave (when a second instance is actually needed)

6. **Redis rate limiter** — in-memory breaks at 2+ instances (D6/L1):
   each node keeps its own bucket → effective limit = N×.
7. **Sweeper leader election** — two sweepers will fight over due
   work; claim via PG/Redis.
8. **`db.Database.Migrate()` at startup** — two instances can race a
   migration (EF Migrate is not concurrency-safe). Options: migration
   as a separate CI job/step, or a PG advisory lock.
9. **Wire in `ConsistentHashRing`** — ready and tested, waits for a
   reason to exist.
10. ~~`/stats` seq scan~~ ✓ closed by `StatsCache` (2026-10-26): one
    snapshot serves /stats+/metrics, TTL 3s.

## Portfolio finale

11. ~~**EN docs** (TASK-011)~~ — core docs done 26.10.
12. **Live demo under load** — dedup/retries/DLQ in front of a viewer;
    defend "where's the dup, where's the loss" from the boundary map.
