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
- **Retries** — in-memory min-heap with versioned lazy deletion over
  a durable Postgres schedule; exponential backoff + equal jitter;
  exhausted events land in a queryable DLQ, replayable after fixes.
- **Signed webhooks** — `X-Webhook-Signature: sha256=<hex>` HMAC over
  the payload per endpoint secret.
- **Observability** — `/metrics` counters + ratios, PG/Redis readiness
  probe, SignalR status feed and a React dashboard (status cards,
  metrics strip, event list with attempt history, DLQ replay).

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
  WebhookEngine.Api            HTTP ingest, SignalR, DI wiring, workers
  WebhookEngine.Core           domain models + delivery-guarantee contracts
  WebhookEngine.Infrastructure Postgres (EF Core), Redis, external plumbing
dashboard/                     React + TS ops dashboard (stats, metrics, events, DLQ, live feed)
tools/LoadGen/                 load generator (rps, burst, duplicate rate)
tools/MockReceiver/            test webhook receiver with chaos modes (500/429/slow/flap/drop)
tests/
  WebhookEngine.Tests          unit + chaos tests
docs/                          architecture spec, task backlog, diagrams
  guides/                      concept guides (RU): hands-on tasks, stack Q&A
PLAN.md                        data flow map + decision journal (ADR-style)
```

## Getting started

```bash
docker compose -p webhook-engine up        # api + postgres + redis
# or run deps only and the API locally:
docker compose -p webhook-engine up -d postgres redis
dotnet run --project src/WebhookEngine.Api # http://localhost:5269
cd dashboard && npm install && npm run dev # http://localhost:5173
```

## API surface

```
POST /webhooks                    register endpoint (url, secret, rate limit)
GET  /webhooks                    list endpoints
POST /events                      ingest (idempotent; 202 accepted / duplicate)
GET  /events?take=&status=        recent events
GET  /events/{id}                 event detail + attempt history
GET  /deadletters                 DLQ (latest 200, live entries)
POST /deadletters/{id}/replay     replay a dead-lettered event (bypasses dedup)
GET  /stats                       status counts + live DLQ count
GET  /metrics                     counters, dedup/retry ratios, queue depth
GET  /health                      readiness: Postgres + Redis ping (503 on degrade)
GET  /hubs/status                 SignalR status feed
```

## Status

Full delivery loop implemented end-to-end: ingest with dedup → atomic
claim → bounded queue → parallel workers → token bucket → signed send →
backoff/jitter retries → DLQ + replay — with metrics, readiness, and a
live dashboard. Remaining: chaos-scenario tests, Docker verification,
multi-instance concerns (Redis-backed limiter, leader election). See
[docs/tasks.md](docs/tasks.md) for the backlog and
[docs/next.md](docs/next.md) for the construction queue.

## License

MIT
