# Database — model, rationale, behavior under load

PostgreSQL is the **durable spine** of the engine: the source of truth
for every event, the durable retry schedule, and the hard
deduplication boundary. Redis is only an accelerator in front of it.
If Redis lies, PG still does not.

Diagrams: `diagrams/mmd/db.mmd` (ERD, renders on GitHub) ·
`diagrams/schema.sql` (hand-mirrored DDL — migrations are the truth) ·
`diagrams/io/db.drawio` (editable source).

The EF model lives in `Infrastructure/Persistence` +
`Core/Models`; migrations in `Infrastructure/Migrations` —
**the migration snapshot is the authority**, this file is the
explanation a newcomer should read first.

---

## Tables

### `Webhooks` — registered endpoints

| Column | Why it exists |
| --- | --- |
| `Id` uuid PK | public identifier handed to ingest callers |
| `Url` varchar(2048), indexed | delivery target; indexed because it's a lookup field for tools, not for hot paths |
| `Secret` text, nullable | HMAC key — `X-Webhook-Signature: sha256=` is sent only when present |
| `RateLimitPerSecond` int | token-bucket rate per webhook — protects *the receiver*, not the engine |
| `IsActive` bool | soft switch: inactive webhook = non-retryable failure at send time |
| `CreatedAt` | audit |

Deleting a webhook cascades to its `Events` → `DeliveryAttempts` →
`DeadLetters` (FK `ON DELETE CASCADE`).

### `Events` — the durable queue (the heart)

Every accepted event is INSERTed **before** the 202 is returned —
that is the entire at-least-once guarantee: once acked, the row
exists and *some* path will drive it to a terminal status.

| Column | Why it exists |
| --- | --- |
| `Id` uuid PK | event id returned to the caller |
| `WebhookId` FK → Webhooks | delivery target, cascade |
| `IdempotencyKey` varchar(64), **UNIQUE** | `sha256(webhookId:payload:bucket)` — the hard dedup boundary. Two inserts, one wins; the loser gets `23505` and reads the winner's id. This is why loss-after-accept is impossible even with Redis dead. |
| `Payload` jsonb | raw JSON string; never parsed by the engine |
| `Status` int | the state machine column — see `diagrams/mmd/state-machine.mmd`. `0 Pending · 1 InFlight · 2 RetryScheduled · 3 Delivered · 4 DeadLettered` |
| `AttemptCount` int | delivery attempts made; **resets semantics**: replayed events keep history, so numbers repeat across cycles — chronological order is by `AttemptedAt`, not this column |
| `ClaimVersion` int | lease-version column prepared for H-3 (optimistic claim guard against the Q7 parallel-dup window) |
| `NextAttemptAt` timestamptz? | durable retry schedule — a retry survives a process restart because the *time* lives here, not in memory |
| `DeliveredAt` timestamptz? | delivery stamp |
| `CreatedAt` | ingest stamp |
| `UpdatedAt` timestamptz? | lease heartbeat — the sweeper reclaims `InFlight` rows whose `UpdatedAt` is older than the lease timeout |

### `DeliveryAttempts` — attempt history (append-only)

| Column | Why it exists |
| --- | --- |
| `Id` bigint identity PK | monotonic identity, not uuid — attempts are high-volume append-only rows |
| `EventId` FK → Events | owner |
| `AttemptNumber` int | per-cycle counter; repeats after replay — **order by `AttemptedAt`, never by number** |
| `AttemptedAt` | chronological truth |
| `ResponseStatusCode` int? | null on timeout/connect failure — the receiver never answered |
| `Error` text | timeout / conn error / `HTTP xxx` |
| `Succeeded` bool | 2xx-3xx |
| `DurationMs` int | latency per attempt — the timeout boundary (~10s) is visible here |

### `DeadLetters` — terminal + audit

Not a single row per event — **an audit trail**: replay re-processes
the event; if it dies again a *new* row is written and the old one
gets `ReplayedAt` set. `ReplayedAt IS NULL` = the live DLQ.

| Column | Why it exists |
| --- | --- |
| `Id` bigint identity PK | like attempts — append-only |
| `EventId` FK → Events | owner |
| `Reason` text | human-readable terminal cause (`max attempts (5) exhausted: HTTP 500`, `webhook missing or inactive`, ...) |
| `DeadLetteredAt` | ordering index for the DLQ feed |
| `ReplayedAt` timestamptz? | null = live entry; non-null = superseded audit row |

---

## Indexes — what each one buys

| Index | Buyer | Decision |
| --- | --- | --- |
| `IX_Events_IdempotencyKey` UNIQUE | ingest dedup — every accept or duplicate goes through it | the guarantee itself, non-negotiable |
| `IX_Events_Status_NextAttemptAt` | sweeper due-scan: `(Status IN (0,2)) AND NextAttemptAt <= now` | full index kept — L4 was resolved by EXPLAIN (TASK-010): partial variants added nothing, ~0.07 ms scans |
| `IX_Events_WebhookId` | FK + event listing per webhook | supporting index |
| `IX_DeliveryAttempts_EventId_AttemptNumber` | event detail panel | history fetch |
| `IX_DeadLetters_EventId`, `IX_DeadLetters_DeadLetteredAt` | DLQ feed ordering | supporting indexes |
| `IX_Webhooks_Url` | tooling lookup | not a hot path |

No index on `UpdatedAt` — the stale-reclaim scan
(`Status=InFlight AND UpdatedAt < cutoff`) is rare and runs inside
the composite `(Status, NextAttemptAt)` scan's cost envelope; L4
analysis showed the full index covers both.

---

## Behavior under load — measured, not guessed

Load tool: `tools/LoadGen` (`--rps --seconds --burst --dup`).

### Sustained 300 rps × 15 s (4 500 requests, 20% duplicates)

- **0 errors.** 3 616 accepted + 884 dedup hits — exactly the
  configured ratio.
- Dedup ratio ~17.5% on `/metrics`, `queue_depth` returned to 0 —
  delivery drained as fast as ingest arrived.

### Burst 2 500 rps × 12 s (~29 600 requests)

- **0 server-side exceptions** — every request that reached the API
  was processed (accept/dedup); nothing 500'd.
- ~13k client-side errors were **LoadGen socket exhaustion** — a
  single process on one machine cannot open 2 500 concurrent
  connections (ephemeral ports). The engine outlasted the client.
- **The real boundary found — connection pool:**
  Npgsql `MaxPoolSize` defaults to **100**, which equals Postgres
  `max_connections=100`. Under the burst the pool grew to 100 and
  *held* the entire server:
  - external clients (`psql`, other apps) → `FATAL: too many clients`
  - `/health` reported `degraded / postgres:false` while the pool
    was saturated — `CanConnectAsync` could not acquire a pooled
    connection in time
  - recovery: connections drain via Npgsql `IdleLifetime` (~300 s)
    or on process restart
- **Fixed — `Maximum Pool Size=80`** is now set in
  `appsettings.json`, `appsettings.Development.json` **and** the
  compose `ConnectionStrings__Postgres` (all three — the dev file
  silently overrides the default one, which is how this almost
  slipped through). Re-verified under the same burst: pool capped
  at exactly 80 (80 idle + 1 active), `psql` connects freely,
  `/health` stays `ok`. For multi-instance deployments remember:
  N instances × 80 must stay under `max_connections` — scale wave
  item: PgBouncer or a raised limit.

### Channel-full path (capacity 10 000)

`Queue:Capacity` bounded channel, `TryEnqueue` fails when full → the
handler **rolls the event back to `Pending`** and the sweeper picks
it up on the next tick. Full queue = slower delivery, never a drop.
Same rollback exists in the sweeper's own enqueue path.

### What breaks first (in order)

1. **PG connection pool** — before CPU, before the channel. Watch
   `pg_stat_activity` / health under load.
2. **Outbound HTTP to receivers** — `DurationMs` in attempts grows;
   with a slow endpoint, retries accumulate in `RetryScheduled`
   (visible as `dlq_live`/`byStatus` shift).
3. **In-memory accelerators** — rate limiter and retry heap are
   single-instance (documented scale-wave items: Redis limiter,
   leader election).
4. The channel at 10 000 — only when delivery is slower than ingest
   for ~tens of thousands of events; even then it degrades to
   sweeper-paced delivery, not loss.
