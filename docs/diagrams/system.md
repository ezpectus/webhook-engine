# Диаграммы системы

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
  fail-open → индекс решает).
- `(Status, NextAttemptAt)` — кормит due-scan sweeper'а; кандидат на
  partial index `WHERE Status IN (Pending, RetryScheduled)` — вопрос L4.
- `UpdatedAt` — и lease, и «когда что-то трогали» — двойная роль.
