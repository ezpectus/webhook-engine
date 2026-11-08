# Contributing

This repository is a **learning artifact first, a project second**.
It exists to demonstrate — honestly — how at-least-once webhook
delivery works: where duplicates can happen (and why they're legal),
and where loss could happen (and why it must not).

## Who this is for

- Engineers who want to *see* delivery guarantees work under real
  failure — not read a blog post about them.
- People reviewing the code as a portfolio piece: every design
  decision is written down in `PLAN.md` and `docs/`, including the
  bugs found in review and how they were fixed.
- Anyone who wants a playground: `docker compose up` gives you a
  full pipeline plus a dashboard where you can fire chaos-mode
  events and watch retries/DLQ happen live.

## Ground rules for changes

1. **The guarantee boundaries are sacred.** Durable-first ordering
   (PostgreSQL row before channel enqueue), the 23505 → duplicate
   contract, lease reclaim, replay semantics — changes here need a
   written reason in `PLAN.md` and a test or live proof, not just
   a diff.
2. **Docs describe code as it is**, not as intended. Changing
   behavior without updating `docs/guide.md` / boundary maps is a
   rejected PR.
3. **Honest failures over graceful-looking lies.** Fail-open at the
   Redis dedup boundary is intentional (PG is the hard boundary);
   "fixes" that silently drop or hide work are not fixes.
4. **Small commits, plain messages**, no tool attribution. Tests
   ship with code changes (`dotnet test` + `node tests/smoke.mjs`
   for live behavior).
5. **English code comments**; docs exist in EN (authoritative) and
   RU (process log — kept intentionally in `docs/ru/`).

## Local verification

```bash
dotnet build && dotnet test            # 56 unit tests
docker compose up -d postgres redis
dotnet run --project src/WebhookEngine.Api &
node tests/smoke.mjs                   # 20 live checks
```

## Good first contributions

- `docs/next.md` is the construction queue (scale wave: Redis rate
  limiter, leader election, hash-ring wiring).
- `docs/tasks.md` H-2/H-3 are scoped hands-on tickets with
  walkthroughs in `docs/guides/`.
- More chaos modes for `tools/MockReceiver` (jitter, partial body,
  slow-then-flap) are always useful for demos.
