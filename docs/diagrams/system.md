# System diagrams

Editable sources: `mmd/` — mermaid (`system.mmd` architecture,
`db.mmd` ER schema, `classes.mmd` class map, `dedup-flow.mmd` the
H-1 dedup flow, `state-machine.mmd` event statuses, `sequence.mmd`
request lifecycle); `io/` — draw.io (`system.drawio`, `db.drawio`,
`classes.drawio`); `schema.sql` — DDL. Mermaid renders directly on
GitHub; drawio opens in app.diagrams.net or the VS Code extension.

## Architecture (components)

```mermaid
flowchart TB
    subgraph Clients["Clients"]
        C1[Client services]
    end

    subgraph Ingest["Ingest"]
        API[Ingest API<br/>POST /events]
    end

    subgraph Dur["Durability"]
        PG[(PostgreSQL<br/>events · attempts · dead_letters)]
        RDS[(Redis<br/>dedup keys · rate-limit state)]
    end

    subgraph Pipe["Delivery pipeline"]
        Q[(Bounded channel<br/>backpressure)]
        W[Worker pool]
        RL{Token bucket<br/>per webhook}
        RH[Retry heap<br/>backoff + jitter]
        SW[Sweeper<br/>lease reclaim + due scan]
        DLQ[(Dead letters)]
    end

    subgraph Obs["Observability"]
        SIG[SignalR hub]
        DASH[React dashboard]
    end

    C1 -->|POST /events| API
    API --> RDS
    API --> PG
    PG --> Q --> W --> RL
    RL -->|HTTP| EXT[External webhook endpoints]
    W -->|status| SIG --> DASH
    RL -.->|exhausted| DLQ
    SW -->|Pending → InFlight| Q
    SW -.->|stale InFlight → Pending| PG
    DASH -->|replay| Q
    API -->|status| SIG
```

## Data flow with failure paths

```mermaid
flowchart LR
    C[Client] -->|POST /events| A[Ingest API]
    A --> V{Validation}
    V -->|invalid| R4[400 reject]
    V -->|ok| D{Dedup: Redis SET NX}
    D -->|dup| R2[200 + event id<br/>duplicate]
    D -->|new| DB[(PG: events · Pending)]
    DB --> Q[(Channel: bounded)]
    Q --> W[Worker pool]
    W --> RL{Rate limiter}
    RL -->|ok| S[HTTP → webhook]
    S -->|2xx| ACK[(PG: Delivered + DeliveredAt)]
    S -->|fail| H[Retry: heap / NextAttemptAt]
    H -->|due| W
    H -->|max attempts| DLQ[(DeadLetters + reason)]
```

Loss zones (see [../failure-map.md](../failure-map.md) — the
dup/loss table): an event is unlosable only once it sits in `PG`.
Everything before that is either refused to the client or the
client retries itself.

## Dedup flow (H-1, rolling window)

```mermaid
flowchart TD
    A[POST /events] --> B{webhook + valid JSON?}
    B -- no --> R4[400]
    B -- yes --> C["redisKey = sha256(wh:payload)<br/>pgKey = sha256(wh:payload:bucket)"]
    C --> D{"GET dedup:&lt;redisKey&gt;"}
    D -- "hit: eventId" --> R2["200 duplicate<br/>id from Redis value, PG untouched"]
    D -- "miss / Redis down<br/>fail-open" --> E["INSERT IdempotencyKey = pgKey"]
    E -- "23505" --> R5["200 duplicate<br/>hard boundary"]
    E -- ok --> F["claim InFlight → enqueue"]
    F --> G["SET dedup:&lt;redisKey&gt; = eventId<br/>NX EX window — TTL from first sight"]
    G --> R3[202 accepted]
```

The Redis marker is the fast path (window rolls on TTL from first
sight); the bucketed PG key is the hard window boundary (an index
can't be "by time", so time is baked into the key). Residual
degradation: Redis dead + two POSTs on opposite sides of the bucket
edge → both insert — a legal duplicate.

## Request lifecycle (sequence)

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant A as Ingest API
    participant R as Redis
    participant P as PostgreSQL
    participant Q as Bounded channel
    participant W as Worker ×N
    participant L as Rate limiter
    participant E as Webhook endpoint
    participant H as StatusHub (SignalR)

    C->>A: POST /events {webhookId, payload}
    A->>A: validate: webhook exists+active, JSON payload
    A->>R: GET dedup-key sha256(wh:payload)
    alt marker hit
        R-->>A: eventId
        A-->>C: 200 duplicate {eventId} — zero PG reads
    else miss / Redis down (fail-open)
        R-->>A: nil / connection error
        A->>P: INSERT Event {IdempotencyKey: sha256(wh:payload:bucket), Pending}
        alt 23505 unique violation
            P-->>A: conflict
            A-->>C: 200 duplicate {eventId} — hard boundary
        else inserted
            P-->>A: ok
            A->>P: UPDATE claim → InFlight (atomic)
            A->>Q: TryEnqueue
            alt channel full
                Q-->>A: false → stays Pending (sweeper picks up)
            end
            A->>R: SET dedup-key = eventId NX EX window
            A-->>C: 202 accepted {eventId}
        end
    end

    Q->>W: dequeue (only InFlight items)
    W->>L: wait permit (token bucket per webhook)
    L-->>W: allowed / wait → RetryScheduled (not an attempt)
    W->>E: POST payload + HMAC signature (10s timeout)
    alt 2xx
        E-->>W: 200
        W->>P: attempt row + Delivered + DeliveredAt
    else retryable failure
        E-->>W: 5xx/429/timeout
        W->>P: attempt row + RetryScheduled + NextAttemptAt = now + backoff·2^a + jitter
        Note over W,P: attempts exhausted → DeadLettered + DeadLetterItem(reason)
    end
    W->>H: publish status change
    H-->>C: live feed update (dashboard)
```

## Status machine (`EventStatus`)

```mermaid
stateDiagram-v2
    [*] --> Pending : ingest, channel full
    [*] --> InFlight : ingest, channel accepted

    Pending --> InFlight : sweeper claim<br/>due-scan or heap
    InFlight --> Delivered : send 2xx<br/>attempt + DeliveredAt
    InFlight --> RetryScheduled : retryable fail<br/>AttemptCount below max<br/>NextAttemptAt = now + backoff
    InFlight --> DeadLettered : non-retryable (4xx, bad payload)<br/>or attempts exhausted
    RetryScheduled --> InFlight : sweeper claim<br/>NextAttemptAt ≤ now
    InFlight --> Pending : lease expired<br/>stale reclaim (InFlightTimeout)
    DeadLettered --> Pending : manual replay<br/>AttemptCount → 0, bypass dedup

    Delivered --> [*]
    DeadLettered --> [*] : terminal (kept as audit)

    note right of InFlight
        Lease = UpdatedAt timestamp.
        Crash between send and ack →
        reclaim → repeated send.
        Legal at-least-once dup (В7).
    end note

    note right of RetryScheduled
        Truth = PG NextAttemptAt.
        Heap in memory = accelerator only,
        rebuilt by scan after restart.
    end note
```

## Database schema (ER)

```mermaid
erDiagram
    WebhookEndpoints ||--o{ NotificationEvents : "receives"
    NotificationEvents ||--o{ DeliveryAttempts : "history"
    NotificationEvents ||--o| DeadLetters : "terminal"

    WebhookEndpoints {
        uuid Id PK
        string Url "IDX"
        string Secret "HMAC signing key"
        int RateLimitPerSecond "token bucket"
        bool IsActive
        timestamptz CreatedAt
    }

    NotificationEvents {
        uuid Id PK
        uuid WebhookId FK
        string IdempotencyKey "UNIQUE — dedup boundary"
        jsonb Payload
        int Status "Pending·InFlight·RetryScheduled·Delivered·DeadLettered"
        int AttemptCount
        int ClaimVersion "lease version for H-3 (optimistic claim)"
        timestamptz NextAttemptAt "durable schedule; IDX(Status,NextAttemptAt)"
        timestamptz DeliveredAt
        timestamptz CreatedAt
        timestamptz UpdatedAt "lease mark for stale reclaim"
    }

    DeliveryAttempts {
        uuid Id PK
        uuid EventId FK "IDX(EventId,AttemptNumber)"
        int AttemptNumber
        timestamptz AttemptedAt
        int ResponseStatusCode "null on timeout/conn error"
        string Error
        bool Succeeded
        int DurationMs
    }

    DeadLetters {
        uuid Id PK
        uuid EventId FK
        string Reason "why it's here — must read clearly a week later"
        timestamptz DeadLetteredAt "IDX"
        timestamptz ReplayedAt "null = live DLQ entry"
    }
```

Schema notes:
- `IdempotencyKey` UNIQUE — the last correctness boundary (Redis
  fail-open → the index decides). H-1: the key carries the time
  bucket — unique holds the window, not eternity.
- `(Status, NextAttemptAt)` — feeds the sweeper's due-scan; resolved
  by EXPLAIN (TASK-010) — full index kept, partial not needed.
- `UpdatedAt` — both lease and "when something was touched" —
  a double role.
- `ClaimVersion` — the H-3 column: claim increments it, a worker
  writes its result only under its own version (stale-claim guard).
