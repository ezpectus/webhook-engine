# What's next — the queue

As of 2026-10-29. The full delivery loop is implemented, wired, and
**verified live on the containerized stack** — 9 chaos failures
reproduced, three LoadGen runs (2,055 / ~4,800 / 803 accepted),
zero loss after accept.
Order below is by dependencies, not importance.

## Blocking now

1. **User's final story** — "where it can dup / where it can lose,
   and why that's OK/not OK" against the boundary map in `PLAN.md`.
   That's the project's acceptance criterion. Ticket-level checkpoint
   questions for TASK-001/002/003 are all answered; what remains is
   one coherent pass over the map.
2. **TASK-011 remainder** — core docs are EN; working artifacts
   (tasks/done-log/open_questions/CHANGELOG) stay RU by design —
   they're the process log, not the portfolio face.

## Public-repo readiness (audited 2026-10-29)

Done in `pending-commit`:
- `.dockerignore` — excluded `dashboard/`, `tools/`, `**/node_modules`,
  `**/dist` from the API build context (was shipping node_modules).
- README — `compose up` now lists all 5 services + ports, sanity-check
  curl, mockreceiver chaos-modes hint.
- `.github/workflows/ci.yml` — dotnet build+test and dashboard
  lint+build on push/PR.
- Fixed one dead link in `docs/ru/architecture.md`.

Verified clean:
- No secrets — only local dev creds (`engine/engine`) in
  appsettings/compose, intentionally visible for a demo stack.
- `.gitignore` covers build/node/editor artifacts; nothing stray
  tracked. MIT LICENSE present. `compose config` valid. 40/40 tests.
- `db.Database.Migrate()` at startup → `compose up` works from zero.

Still missing for "fully ready":
- Repo metadata on GitHub: description, topics (`dotnet`, `webhooks`,
  `distributed-systems`, `at-least-once`, `postgres`, `redis`),
  homepage link — set in repo settings, not in code.
- CI badge in README once the first workflow run is green.
- A demo GIF/screenshot of the dashboard in README — the strongest
  portfolio artifact for this project (needs a live stack + capture).
- Optional: `SECURITY.md`/`CONTRIBUTING.md` — low value for a solo
  portfolio repo.

## Next wave (optional hands-on — the user's practice lane)

3. **Q8 fix — honest dedup window (H-1)**: design locked 2026-10-28 —
   rolling window in Redis (TTL from first sight, content key) +
   bucketed PG key as backstop. Decisions in
   `guides/h1_walkthrough.md`; ~30 lines in `IngestEvent` + tests.
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
