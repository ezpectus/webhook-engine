# webhook-engine

A real-time notification & webhook delivery engine built around one idea:
**at-least-once delivery is a system property, not a feature**.

It accepts events over HTTP and delivers them to subscribed webhook
endpoints — reliably, under load, and honestly about failure.

## What it guarantees

- **At-least-once delivery** — an accepted event is persisted before it's
  acknowledged. Duplicates are acceptable by design; loss is not.
- **Idempotent ingest** — dedup via Redis `SET NX` on a SHA256 key, with a
  Postgres unique constraint as the hard boundary underneath.
- **Backpressure** — bounded channels with an explicit overflow policy
  instead of silent OOM.
- **Per-webhook rate limiting** — token bucket per endpoint, so receivers
  don't ban us.
- **Retries** — min-heap scheduler with exponential backoff and jitter;
  exhausted events land in a queryable DLQ, replayable after fixes.
- **Observability** — delivery status feed over SignalR and a React
  dashboard (delivery stats, DLQ inspection, manual retry).

## Stack

.NET (C#) · PostgreSQL + EF Core · Redis · SignalR · React + TypeScript ·
docker-compose

## Architecture

```
POST /events → validate → dedup (Redis) → persist (PG, Pending)
             → bounded channel → workers → rate limiter → send
             → 2xx: Delivered · fail: retry heap → exhausted: DLQ
```

Diagrams and the reasoning behind the ordering live in
[docs/diagrams/system.md](docs/diagrams/system.md) and
[PLAN.md](PLAN.md).

## Repository layout

```
src/
  WebhookEngine.Api            HTTP ingest, SignalR, DI wiring
  WebhookEngine.Core           domain models + delivery-guarantee contracts
  WebhookEngine.Infrastructure Postgres (EF Core), Redis, external plumbing
tests/
  WebhookEngine.Tests          unit + chaos tests
docs/                          architecture spec, task backlog, diagrams
PLAN.md                        data flow map + decision journal (ADR-style)
```

## Getting started

```bash
docker compose -p webhook-engine up        # api + postgres + redis
# or run deps only and the API locally:
docker compose -p webhook-engine up -d postgres redis
dotnet run --project src/WebhookEngine.Api # http://localhost:5xxx/health
```

## Status

Early build stage — scaffold, schema and compose are in; the delivery
core is being implemented stage by stage. See
[docs/tasks.md](docs/tasks.md) for the backlog and
[docs/architecture.md](docs/architecture.md) for the roadmap.

## License

MIT
