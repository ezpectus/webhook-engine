-- Схема БД webhook-engine (зеркало EF Core модели,
-- WebhookEngineDbContext.OnModelCreating). Сгенерировано для обзора —
-- актуальным источником правды являются миграции.

CREATE TABLE "Webhooks" (
    "Id" uuid PRIMARY KEY,
    "Url" varchar(2048) NOT NULL,
    "Secret" text,
    "RateLimitPerSecond" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);
CREATE INDEX "IX_Webhooks_Url" ON "Webhooks" ("Url");

CREATE TABLE "Events" (
    "Id" uuid PRIMARY KEY,
    "WebhookId" uuid NOT NULL REFERENCES "Webhooks" ("Id") ON DELETE CASCADE,
    "IdempotencyKey" varchar(64) NOT NULL,
    "Payload" jsonb NOT NULL,
    "Status" integer NOT NULL,          -- 0 Pending · 1 InFlight · 2 RetryScheduled · 3 Delivered · 4 DeadLettered
    "AttemptCount" integer NOT NULL,
    "ClaimVersion" integer NOT NULL DEFAULT 0,  -- lease-versioning для H-3 (optimistic claim guard)
    "NextAttemptAt" timestamptz,        -- durable-расписание ретраев
    "DeliveredAt" timestamptz,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NOT NULL    -- lease-метка для stale-reclaim
);
-- H-1: ключ = sha256(wh:payload:bucket) — unique держит окно, не вечность
CREATE UNIQUE INDEX "IX_Events_IdempotencyKey" ON "Events" ("IdempotencyKey");
CREATE INDEX "IX_Events_WebhookId" ON "Events" ("WebhookId");
-- кормит due-scan sweeper'а; L4 решён EXPLAIN'ом (TASK-010): полный держим
CREATE INDEX "IX_Events_Status_NextAttemptAt" ON "Events" ("Status", "NextAttemptAt");

CREATE TABLE "DeliveryAttempts" (
    "Id" uuid PRIMARY KEY,
    "EventId" uuid NOT NULL REFERENCES "Events" ("Id") ON DELETE CASCADE,
    "AttemptNumber" integer NOT NULL,
    "AttemptedAt" timestamptz NOT NULL,
    "ResponseStatusCode" integer,       -- null при timeout/conn error
    "Error" text,
    "Succeeded" boolean NOT NULL,
    "DurationMs" integer NOT NULL
);
CREATE INDEX "IX_DeliveryAttempts_EventId_AttemptNumber"
    ON "DeliveryAttempts" ("EventId", "AttemptNumber");

CREATE TABLE "DeadLetters" (
    "Id" uuid PRIMARY KEY,
    "EventId" uuid NOT NULL REFERENCES "Events" ("Id") ON DELETE CASCADE,
    "Reason" text NOT NULL,
    "DeadLetteredAt" timestamptz NOT NULL,
    "ReplayedAt" timestamptz            -- null = живая запись DLQ
);
CREATE INDEX "IX_DeadLetters_EventId" ON "DeadLetters" ("EventId");
CREATE INDEX "IX_DeadLetters_DeadLetteredAt" ON "DeadLetters" ("DeadLetteredAt");

-- L4 решён (EXPLAIN, TASK-010): partial-варианты ниже не нужны —
-- полный индекс покрывает оба скана за ~0.07мс. Оставлено как история.
-- CREATE INDEX "IX_Events_DueScan" ON "Events" ("NextAttemptAt")
--     WHERE "Status" IN (0, 2);        -- только живые статусы
-- CREATE INDEX "IX_Events_StaleScan" ON "Events" ("UpdatedAt")
--     WHERE "Status" = 1;              -- InFlight-lease
