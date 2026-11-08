# Диаграммы системы

Файлы-источники (редактируемые): `mmd/` — mermaid (`system.mmd`
архитектура, `db.mmd` ER-схема, `classes.mmd` карта классов,
`dedup-flow.mmd` поток дедупликации H-1, `state-machine.mmd`
статусы события, `sequence.mmd` жизненный цикл запроса); `io/` —
draw.io (`system.drawio`, `db.drawio`, `classes.drawio`); `schema.sql`
— DDL. Mermaid рендерится прямо на GitHub; drawio открывается в
app.diagrams.net или расширением VS Code.

## Архитектура (компоненты)

```mermaid
flowchart TB
    subgraph Clients["Клиенты"]
        C1[Client services]
    end

    subgraph Ingest["Приём"]
        API[Ingest API<br/>POST /events]
    end

    subgraph Dur["Персистентность"]
        PG[(PostgreSQL<br/>events · attempts · dead_letters)]
        RDS[(Redis<br/>dedup keys · rate-limit state)]
    end

    subgraph Pipe["Пайплайн доставки"]
        Q[(Bounded channel<br/>backpressure)]
        W[Worker pool]
        RL{Token bucket<br/>per webhook}
        RH[Retry heap<br/>backoff + jitter]
        SW[Sweeper<br/>lease reclaim + due scan]
        DLQ[(Dead letters)]
    end

    subgraph Obs["Наблюдаемость"]
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

## Data flow с путями сбоев

```mermaid
flowchart LR
    C[Client] -->|POST /events| A[Ingest API]
    A --> V{Валидация}
    V -->|невалид| R4[400 reject]
    V -->|ok| D{Dedup: Redis SET NX}
    D -->|дубль| R2[200 + event id<br/>duplicate]
    D -->|новое| DB[(PG: events · Pending)]
    DB --> Q[(Channel: bounded)]
    Q --> W[Worker pool]
    W --> RL{Rate limiter}
    RL -->|ok| S[HTTP → webhook]
    S -->|2xx| ACK[(PG: Delivered + DeliveredAt)]
    S -->|fail| H[Retry: heap / NextAttemptAt]
    H -->|due| W
    H -->|max attempts| DLQ[(DeadLetters + reason)]
```

Красные зоны потери (см. `PLAN.md` — карта «дубль/потеря»):
событие непотеряемо только после записи в `PG`. Всё, что до неё —
либо отвергнуто клиенту, либо клиент ретраит сам.

## Dedup-поток (H-1, rolling-окно)

```mermaid
flowchart TD
    A[POST /events] --> B{webhook + валидный JSON?}
    B -- нет --> R4[400]
    B -- да --> C["redisKey = sha256(wh:payload)<br/>pgKey = sha256(wh:payload:bucket)"]
    C --> D{"GET dedup:&lt;redisKey&gt;"}
    D -- "hit: eventId" --> R2["200 duplicate<br/>id из значения Redis, PG не тронута"]
    D -- "miss / Redis down<br/>fail-open" --> E["INSERT IdempotencyKey = pgKey"]
    E -- "23505" --> R5["200 duplicate<br/>жёсткая граница"]
    E -- ok --> F["claim InFlight → enqueue"]
    F --> G["SET dedup:&lt;redisKey&gt; = eventId<br/>NX EX window — TTL от первого вида"]
    G --> R3[202 accepted]
```

Redis-маркер — быстрый путь (окно катится TTL от первого вида);
бакетный PG-ключ — жёсткая граница окна (индекс не умеет «по времени»,
время зашито в ключ). Остаточная деградация: Redis мёртв + два POST'а
по разные стороны стыка бакета → оба вставятся — легальный дубль.

## Жизненный цикл запроса (sequence)

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

## Машина статусов (`EventStatus`)

```mermaid
stateDiagram-v2
    [*] --> Pending : ingest, channel full
    [*] --> InFlight : ingest, channel accepted

    Pending --> InFlight : sweeper claim<br/>due-scan or heap
    InFlight --> Delivered : send 2xx<br/>attempt + DeliveredAt
    InFlight --> RetryScheduled : retryable fail<br/>AttemptCount below max<br/>NextAttemptAt = now + backoff
    InFlight --> DeadLettered : non-retryable (4xx, bad payload)<br/>or attempts exhausted
    RetryScheduled --> InFlight : sweeper claim<br/>NextAttemptAt 2264 now
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

## Схема БД (ER)

```mermaid
erDiagram
    WebhookEndpoints ||--o{ NotificationEvents : "получает"
    NotificationEvents ||--o{ DeliveryAttempts : "история"
    NotificationEvents ||--o| DeadLetters : "терминал"

    WebhookEndpoints {
        uuid Id PK
        string Url "IDX"
        string Secret "HMAC-ключ подписи"
        int RateLimitPerSecond "token bucket"
        bool IsActive
        timestamptz CreatedAt
    }

    NotificationEvents {
        uuid Id PK
        uuid WebhookId FK
        string IdempotencyKey "UNIQUE — граница dedup"
        jsonb Payload
        int Status "Pending·InFlight·RetryScheduled·Delivered·DeadLettered"
        int AttemptCount
        int ClaimVersion "lease-version для H-3 (optimistic claim)"
        timestamptz NextAttemptAt "дurable-расписание; IDX(Status,NextAttemptAt)"
        timestamptz DeliveredAt
        timestamptz CreatedAt
        timestamptz UpdatedAt "lease-метка для stale-reclaim"
    }

    DeliveryAttempts {
        uuid Id PK
        uuid EventId FK "IDX(EventId,AttemptNumber)"
        int AttemptNumber
        timestamptz AttemptedAt
        int ResponseStatusCode "null при timeout/conn error"
        string Error
        bool Succeeded
        int DurationMs
    }

    DeadLetters {
        uuid Id PK
        uuid EventId FK
        string Reason "почему здесь — через неделю должно быть ясно"
        timestamptz DeadLetteredAt "IDX"
        timestamptz ReplayedAt "null = живая запись DLQ"
    }
```

Заметки по схеме:
- `IdempotencyKey` UNIQUE — последняя граница правильности (Redis
  fail-open → индекс решает). H-1: ключ несёт бакет времени —
  unique держит окно, а не вечность.
- `(Status, NextAttemptAt)` — кормит due-scan sweeper'а; L4 решён
  EXPLAIN'ом (TASK-010) — полный индекс держим, partial не нужен.
- `UpdatedAt` — и lease, и «когда что-то трогали» — двойная роль.
- `ClaimVersion` — колонка под H-3: claim инкрементит её, воркер
  пишет результат только под своей версией (stale-claim guard).
