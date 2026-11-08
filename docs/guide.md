# Guide — how the system works

For me and for anyone joining. Describes the code **as it is**,
not as intended. Last updated 2026-11-07: H-1 honest dedup window
shipped (rolling Redis window + bucketed PG key); boundary
hardening (JSON/http(s) validation, config clamps) verified live.

## Event path end-to-end

```
POST /events
  → validation (JSON payload, webhook exists and is active;
    webhook registration accepts http(s) URLs only)
  → redisKey = sha256(webhookId + ":" + payload)           — content key
    pgKey    = sha256(webhookId + ":" + payload + ":" + bucket)
    bucket   = now / Dedup:WindowHours (TimeProvider)
  → Redis GET dedup:<redisKey>
       ├── hit → return stored eventId → 200 "duplicate" (zero PG reads)
       └── miss / Redis down → pass through (fail-open)
  → backpressure: undelivered >= PendingThreshold → 503
  → INSERT into PG (unique on IdempotencyKey = pgKey;
    23505 → "duplicate" reply — the hard boundary)
  → TryEnqueue into the bounded channel
       ├── fits    → Status = InFlight (atomic claim)
       └── full    → stays Pending (the sweeper picks it up)
  → Redis SET dedup:<redisKey> = eventId, NX EX windowHours
    (rolling TTL from first sight; same content next window = new event)
  → 202 Accepted { eventId }

DeliveryWorker (BackgroundService, ×4 parallel readers)
  → reads the channel → status == InFlight? (stale channel items skipped)
  → rate limiter: wait > 0 → RetryScheduled + NextAttemptAt (not an attempt)
  → WebhookSender.SendAsync (POST + HMAC, 10s timeout)
  → writes a DeliveryAttempt (code, error, duration)
  → 2xx        → Delivered + DeliveredAt        ← this is the ack
  → retryable  → AttemptCount ≥ 5 → DeadLettered + DeadLetterItem
               → else RetryScheduled + NextAttemptAt =
                 now + min(5min, 1s·2^a) + equal jitter; into the heap after save
  → non-retry  → DeadLettered + DeadLetterItem(reason)
  → worker died mid-way → event hangs InFlight → sweeper reclaims it

SweeperService (every 2s locally, gentle)
  → InFlight with UpdatedAt older than 5 min → Pending (lease reclaim)
  → fast path: ReadDueAsync from the heap → claim → enqueue
  → PG scan (truth): Pending/RetryScheduled with NextAttemptAt <= now
    → claim → enqueue
```

## `EventStatus` state machine

| Status | Meaning | Set by |
| --- | --- | --- |
| `Pending` | in PG, waiting for the channel or its time | ingest (channel full), sweeper (reclaim) |
| `InFlight` | in the channel / being worked on (lease) | ingest, sweeper |
| `RetryScheduled` | waiting for `NextAttemptAt` | worker after a retryable failure |
| `Delivered` | ack: the endpoint answered 2xx | worker |
| `DeadLettered` | quarantine, has a `DeadLetterItem` | worker |

## Where the guarantees live (boundary map)

- **Dup at ingest** — two layers: Redis `SET NX` on the content key
  (rolling window, answers with the stored eventId, zero PG reads) and
  the PG unique index on the **time-bucketed** key — the hard boundary
  (`23505` → `duplicate` with the original id). Same content in the
  next window = a new event, not a dup.
- **Residual dup at ingest** — one legal straddle: Redis down +
  identical POSTs on both sides of a bucket edge → different bucketed
  keys → both insert. Redis alive closes it; documented in
  `dedup-flow.mmd` / PLAN.md boundary map.
- **Loss after acceptance** — impossible: everything undelivered is
  a PG row, the sweeper picks up all `Pending`/`RetryScheduled`.
- **Dup on delivery** — possible and legal: worker died after the 2xx
  but before writing the ack → the send repeats.
- **Loss in-flight** — none: the `InFlight` lease expires after 5 min →
  the sweeper returns it to `Pending`.

## File map

| File | What | Zone |
| --- | --- | --- |
| `Api/Endpoints/Endpoints.cs` | POST/GET /events·/webhooks, /deadletters,<br>replay, /stats /metrics /events/{id};<br>threshold, insert-first, atomic claim | user reviews |
| `Api/Hubs/` | SignalR status feed (`/hubs/status`, method `"status"`) | platform |
| `dashboard/` | React dashboard: statuses, metrics, events<br>+ attempts, DLQ+replay, live feed | platform |
| `tools/LoadGen/` | load generator (rps/burst/dup %) | platform |
| `tools/MockReceiver/` | receiver with chaos modes (500/429/slow/flap) | platform |
| `Infrastructure/Redis/RedisDeduplicationStore.cs` | dedup cache, fail-open (abortConnect=false) | user reviews |
| `Infrastructure/Persistence/StatsCache.cs` | 3s TTL snapshot for /stats + /metrics | platform |
| `Infrastructure/Queues/EventQueue.cs` | bounded channel 10k | review |
| `Api/Workers/DeliveryWorker.cs` | ×4 readers; send, attempts, backoff→DLQ | review |
| `Api/Workers/SweeperService.cs` | lease reclaim + heap fast-path + due scan | review |
| `Infrastructure/Delivery/WebhookSender.cs` | HTTP send + HMAC, error classification | platform |
| `Core/RateLimiting/TokenBucket.cs` | token bucket (user's in `/* */` + reference) | review |
| `Infrastructure/RateLimiting/TokenBucketRateLimiter.cs` | ConcurrentDict → bucket per webhook | review (L1/L3) |
| `Core/RetryScheduling/BackoffPolicy.cs` | exp·2^a + equal jitter + cap + MaxAttempts | review (S2–S5) |
| `Core/RetryScheduling/HeapRetryScheduler.cs` | PriorityQueue + versions (lazy deletion) | review |
| `Core/Metrics/EngineMetrics.cs` | boundary counters → /metrics | platform |
| `Core/Hashing/ConsistentHashRing.cs` | ready, not wired — scale wave | review |

## Config (appsettings.json)

Values are **locally-optimal for demo/learning, NOT prod-recommended**
— picked so retries, backpressure and the sweeper are observable live
within seconds. The prod direction is in the last column.

| Key | Default | What it does | Prod direction |
| --- | --- | --- | --- |
| `Dedup:WindowHours` | 24 | dedup window: Redis marker TTL **and** PG bucket granularity (clamped [1h, 8760h], `double`) | Stripe-like, fine |
| `Queue:Capacity` | 10000 | bounded channel size | smaller — bite earlier |
| `Queue:PendingThreshold` | 10000 | 503 when undelivered exceed it | grow per SLO |
| `Sweeper:IntervalSeconds` | 2 | sweep period = retry tick | 5–15s (DB load) |
| `Sweeper:BatchSize` | 500 | batch per sweep | per queue metrics |
| `Sweeper:InFlightTimeoutMinutes` | 5 | lease expiry | ≈ max(delivery+retry window) |
| `Delivery:WorkerParallelism` | 4 | parallel channel readers | 8–16 / CPU |
| `Delivery:SenderTimeoutSeconds` | 10 | HTTP send timeout | 5–30 per recipient SLO |
| `Retry:BaseDelaySeconds` | 1 | exponential base | fine |
| `Retry:MaxDelaySeconds` | 300 | backoff cap | fine |
| `Retry:MaxAttempts` | 5 | ceiling → DLQ | fine |
| `Stats:CacheTtlSeconds` | 3 | snapshot TTL for /stats + /metrics | 5–30 per poll rate |

## Known edges (not bugs — talking points)

- ~~Enqueue race~~ ✓ fixed by the atomic `Pending→InFlight` claim
  (`0829b37`)
- **Q7 — parallel dup via a slow channel**: backlog > 5 min →
  InFlight expires → reclaim → second enqueue → two workers send
  concurrently. The dup is legal; `AttemptCount` loses an increment
  (last-write-wins). Accepted and documented as a boundary; the
  lease-versioning fix is optional hands-on task H-3 in `tasks.md`
- In-memory limiter and heap are single-instance (scale wave: Redis +
  leader election)
- `AttemptCount` is not protected by a concurrency token — see Q7

## Run it

```bash
docker compose -p webhook-engine up        # full stack: api:8080, dashboard:3000
# or deps only + local API:
docker compose -p webhook-engine up -d postgres redis   # PG on host :5433
dotnet run --project src/WebhookEngine.Api              # :5269
dotnet run --project tools/MockReceiver                 # :5005
cd dashboard && npm run dev                              # :5173
# POST /webhooks {"url":"http://localhost:5005/hook?mode=flap","secret":"s3"}
#   → POST /events {"webhookId":"..","payload":"{}"}
# load:
dotnet run --project tools/LoadGen -- --webhook <guid> --rps 100 --dup 0.2
```
