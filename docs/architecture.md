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

## Архитектурный стиль (зафиксировано, D15)

**Модульный монолит.** Три проекта — по ответственности, не по слоям
ради слоёв:

| Проект | Что живёт | Зависит от |
| --- | --- | --- |
| `Core` | модели, абстракции (интерфейсы), чистые алгоритмы | ничего |
| `Infrastructure` | адаптеры: EF/Postgres, Redis, HttpClient | Core |
| `Api` | composition root: DI, эндпоинты, воркеры, хабы | оба |

**Почему именно так, а не иначе:**

- **Не микросервисы.** Гарантия at-least-once держится на границах
  «принял → записал → отправил → ack». Разнести их по сети = умножить
  failure modes без причины: ни одна часть системы не требует
  независимого деплоя или масштабирования прямо сейчас. Узкое место —
  исходящий HTTP и Postgres, а не модульность процесса.
- **Не трёхслойка / не Clean-на-6-проектов.** Core — единственная
  «чистая» зона, и её чистота не декоративная: алгоритмы (bucket,
  heap) тестируются без инфраструктуры вообще. Интерфейс создаём
  только там, где есть реальная граница замены: dedup (Redis↔другой
  кеш), queue, scheduler, limiter, feed. `IRepository` поверх EF
  «потому что надо» — нет.
- **Зависимости смотрят внутрь.** Infrastructure/Api знают Core;
  Core не знает их. Проверяется тем, что Core собирается без
  NuGet-зависимостей на инфраструктуру.

**Куда может эволюционировать** (только при появлении потребности):
несколько инстансов Api → Redis-реализация rate limiter'а, лидер-
элект sweeper'а, consistent hashing (TASK-008). Каждый шаг —
отдельное решение с обоснованием, не «на вырост».

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
- ~~Lease/ownership~~ — реализовано: `InFlight` + `UpdatedAt`, протухание
  через sweeper (`InFlightTimeoutMinutes`). Края: гонка enqueue —
  см. guide.md «Известные края»
- Хранилище отложенных ретраев: сейчас — скан `NextAttemptAt` sweeper'ом;
  in-memory heap придёт с TASK-005 (ускорение, PG остаётся истиной)
- Что входит в `IdempotencyKey` определяет поведение «дубль после окна» —
  решено: sha256(webhookId + payload), клиентский header — эволюция

## Роадмап (стадии)

1. **Каркас** ✓: ASP.NET скелет, Postgres-схема, docker-compose
2. **Приём** ✓: ingest + dedup (insert-first: PG-индекс + Redis-кеш)
3. **Очередь** ✓: bounded channel + политика «PG = очередь, reject при
   Pending > потолка» + sweeper
4. **Доставка** ⏳: workers + HttpClientFactory ✓; rate limiter — TASK-004
5. **Ретраи** ⏳: flat-delay заглушка стоит; min-heap + backoff/jitter —
   TASK-005 (руками)
6. **DLQ**: семантика ✓ (есть в воркере); replay endpoint — TASK-006
7. **Наблюдаемость**: SignalR-фид + React-дашборд — P-05/P-06
8. **Хаос**: тесты сбоев (падение воркера до/после ack и т.п.)
9. **Нагрузка**: генератор «миллион событий», замеры
10. **Подпись**: HMAC-SHA256 подпись webhook'ов по `Secret`
    (заголовок `X-Webhook-Signature`, как `X-Hub-Signature` у GitHub)

Правила сплита (что пишет человек, что — агент) — в `AGENTS.md`.
