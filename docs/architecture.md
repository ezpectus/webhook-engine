# Notification & Webhook Engine — архитектура и спека

Real-time сервис доставки уведомлений (Webhooks, в перспективе Push/Email)
с гарантией **at-least-once delivery**, экспоненциальными ретраями
и rate limiting для сторонних API.

Цель нагрузки: миллионы событий, пики ~1M событий/сек без падения
(упор на backpressure, а не на реальную железную мощность).

## Стек

| Слой | Технология |
| --- | --- |
| Backend | .NET (C#), ASP.NET, `System.Threading.Channels`, `IAsyncEnumerable` |
| Дедупликация / rate limit | Redis |
| Персистентность | PostgreSQL (EF Core миграции) |
| Real-time статус | SignalR |
| Дашборд | React + TypeScript |
| Инфра | docker-compose (app + redis + postgres) |

Mermaid-версии диаграмм: [diagrams/system.md](diagrams/system.md)

## Компоненты

```
            ┌──────────┐   ┌────────────┐   ┌──────────────┐
events ───> │ Ingest   │─> │ Dedup      │─> │ Queue        │
  HTTP      │ API      │   │ (Redis)    │   │ (Channels)   │
            └──────────┘   └────────────┘   └──────┬───────┘
                                                   │
            ┌──────────┐   ┌────────────┐   ┌──────▼───────┐
  SignalR   │ Status   │<──│ Dispatcher │<──│ Rate limiter │
  updates   │ feed     │   │ / workers  │   │ (per-hook)   │
            └──────────┘   └──────┬─────┘   └──────────────┘
                                  │ send
                            ┌─────▼──────┐   ┌──────────────┐
                            │ Webhook    │──>│ Retry        │
                            │ endpoints  │   │ scheduler    │
                            └────────────┘   │ (min-heap)   │
                                  │          └──────┬───────┘
                                  │ exhausted       │ due
                            ┌─────▼──────┐          │
                            │ DLQ        │<─────────┘
                            │ (Postgres) │
                            └────────────┘
```

- **Ingest API** — приём событий, валидация, выдача event id.
- **Dedup** — Redis `SET NX` по SHA256-ключу события, окно дедупликации.
- **Queue** — bounded `Channel`, backpressure-политика при переполнении.
- **Dispatcher/workers** — пул воркеров, консистентное распределение
  webhook'ов по узлам (consistent hashing) при горизонтальном скейле.
- **Rate limiter** — token bucket на каждый webhook endpoint.
- **Retry scheduler** — min-heap отложенных отправок, backoff + jitter.
- **DLQ** — события, исчерпавшие ретраи; просмотр и replay через дашборд.

## Алгоритмы и структуры

- **Token bucket** — контроль RPS на каждый внешний webhook.
- **Priority queue (min-heap)** — отложенные ретраи по `nextAttemptAt`.
- **Consistent hashing** — распределение webhook-воркеров по узлам.
- **SHA256 + Redis SET NX** — идемпотентность на приёме.

## Гарантии доставки

- **At-least-once**: дубли возможны — дедуп на приёме + идемпотентные
  ключи в payload; приёмник должен уметь переваривать повтор.
- **Потеря недопустима**: событие считается доставленным только после
  ack; до ack оно живёт в очереди/retry/DLQ.
- Открытые вопросы границ (где именно может быть дубль/потеря) —
  см. `PLAN.md`.

## Схема БД (стадия 1, реализовано)

- `webhook_endpoints` — Url, Secret, RateLimitPerSecond, IsActive
- `notification_events` — IdempotencyKey (unique), Payload (jsonb),
  Status, AttemptCount, NextAttemptAt, DeliveredAt;
  индекс `(Status, NextAttemptAt)` — скан должных ретраев
- `delivery_attempts` — история попыток: код ответа, ошибка, длительность
- `dead_letter_items` — Reason, DeadLetteredAt, ReplayedAt

**Двойная защита от дублей:** Redis `SET NX` — быстрый фильтр (TTL-окно,
ключи можно потерять при рестарте кеша); unique-индекс в PG — жёсткая
граница: одинаковый `IdempotencyKey` не запишется дважды никогда.
Первый слой — про скорость, второй — про корректность.

**Открытые вопросы схемы** (решаются в тикетах):
- Lease/ownership: как пометить событие «взято воркером», чтобы два
  воркера не взяли одно (поля `LockedUntil`/`LockedBy` или
  `SELECT ... FOR UPDATE SKIP LOCKED`) — к TASK-003/005
- Хранилище отложенных ретраев: in-memory heap vs скан `NextAttemptAt`
  по таблице — что переживает рестарт процесса — к TASK-005
- Что входит в `IdempotencyKey` определяет поведение «дубль после окна» —
  к TASK-002

## Роадмап (стадии)

1. **Каркас**: ASP.NET скелет, Postgres-схема, docker-compose
2. **Приём**: ingest + dedup (Redis)
3. **Очередь**: channels + backpressure
4. **Доставка**: workers + HttpClientFactory + rate limiter
5. **Ретраи**: min-heap scheduler + backoff/jitter
6. **DLQ**: семантика, таблица, replay endpoint
7. **Наблюдаемость**: SignalR-фид + React-дашборд
8. **Хаос**: тесты сбоев (падение воркера до/после ack и т.п.)
9. **Нагрузка**: генератор «миллион событий», замеры

Правила сплита (что пишет человек, что — агент) — в `AGENTS.md`.
