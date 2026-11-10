# Contributing

This repository is a **learning artifact first, a project second**.
It exists to demonstrate — honestly — how at-least-once webhook
delivery works: where duplicates can happen (and why they're legal),
and where loss could happen (and why it must not).

## Who this is for

- Engineers who want to *see* delivery guarantees work under real
  failure — not read a blog post about them.
- People reviewing the code as a portfolio piece: every design
  decision is written down in `docs/` — architecture, the DB
  rationale, and the failure-boundary map proven live.
- Anyone who wants a playground: `docker compose up` gives you a
  full pipeline plus a dashboard where you can fire chaos-mode
  events and watch retries/DLQ happen live.

## Ground rules for changes

1. **The guarantee boundaries are sacred.** Durable-first ordering
   (PostgreSQL row before channel enqueue), the 23505 → duplicate
   contract, lease reclaim, replay semantics — changes here need a
   written reason and a test or live proof, not just a diff.
2. **Docs describe code as it is**, not as intended. Changing
   behavior without updating `docs/guide.md` / boundary maps is a
   rejected PR.
3. **Honest failures over graceful-looking lies.** Fail-open at the
   Redis dedup boundary is intentional (PG is the hard boundary);
   "fixes" that silently drop or hide work are not fixes.
4. **Small commits, plain messages**, no tool attribution. Tests
   ship with code changes (`dotnet test` + `node tests/smoke.mjs`
   for live behavior).
5. **English code comments and docs** — this repo is English-only.

## Local verification

```bash
dotnet build && dotnet test            # 56 unit tests
docker compose up -d postgres redis
dotnet run --project src/WebhookEngine.Api &
node tests/smoke.mjs                   # 20 live checks
```

## Good first contributions

- The scale wave is the open frontier: Redis-backed rate limiter,
  leader election for the sweeper, multi-instance delivery.
- More chaos modes for `tools/MockReceiver` (jitter, partial body,
  slow-then-flap) are always useful for demos.
- A new row in `docs/failure-map.md` with the live proof behind it
  beats ten lines of theory.
