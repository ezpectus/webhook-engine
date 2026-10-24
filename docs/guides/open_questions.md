# Open questions — отвечать в любое время (голосом, по номерам)

Живой сборник открытых вопросов ко мне. Закрытые вычёркиваем,
новые дописываем по мере стадий. К каждому — файлы, где лежит
ответ.

## Карта «что где живёт»

| Файл | Что там |
| --- | --- |
| `src/WebhookEngine.Api/Endpoints/Endpoints.cs` | ingest `IngestEvent`, dedup, потолок, claim, replay, /stats /metrics /events |
| `src/WebhookEngine.Api/Workers/DeliveryWorker.cs` | claim→send→ack, лимитер, backoff, max-attempts→DLQ |
| `src/WebhookEngine.Api/Workers/SweeperService.cs` | stale-lease reclaim, heap fast-path, PG due-scan, claim |
| `src/WebhookEngine.Infrastructure/Queues/EventQueue.cs` | bounded channel, `TryEnqueue` |
| `src/WebhookEngine.Infrastructure/RateLimiting/TokenBucketRateLimiter.cs` | per-webhook buckets (ConcurrentDictionary) |
| `src/WebhookEngine.Core/RetryScheduling/` | `BackoffPolicy` (jitter/cap) + `HeapRetryScheduler` (версии) |
| `src/WebhookEngine.Infrastructure/Persistence/WebhookEngineDbContext.cs` | схема, все индексы |
| `docs/diagrams/schema.sql` | DDL той же схемы для чтения |

## Ревью кода (платформа Devin)

- ~~**В3**~~ ✓ закрыт кодом: атомарный claim `Pending→InFlight` в ingest
  и sweeper (`0829b37`); двойной enqueue исключён
- **В2** ~ частично (2026-10-25): юзер — «не один класс статуса».
  Добивка: все три = недоставленная работа; потолок только на
  Pending слеп к наводнению RetryScheduled · `Endpoints.cs → IngestEvent`
- **В4** ✓ (2026-10-25): страхует `SweeperService` — событие остаётся
  `Pending` в PG, канал лишь ускоритель · `EventQueue.cs` + `SweeperService.cs`
- **В5** ~ подсказкой (2026-10-25): оба пути в `SweeperService` —
  heap `ReadDueAsync` + PG-скан `NextAttemptAt <= now` · `SweeperService.cs`
- **В7** — медленный канал (>5 мин backlog): InFlight с протухшим
  lease → sweeper реклеймит → второй enqueue → два воркера шлют
  параллельно. Дубль легален по at-least-once — но посчитай: что
  случится с `AttemptCount` у обоих (оба прочли N, оба пишут N+1)?
  Чинить ли (lease-version claim) или документировать как допустимую
  границу дубля? · `SweeperService.cs` (stale-reclaim) +
  `DeliveryWorker.cs` (`tracked.AttemptCount++`)
- **DLQ** — `ReplayDeadLetter`: семантика D12 (обход dedup,
  `AttemptCount → 0`, история попыток — аудит). Согласие или правки?
  · `Endpoints.cs → ReplayDeadLetter`
- **API-change** — `AcquireDelayAsync` получила `permitsPerSecond`.
  Ок или спрятать rate внутрь лимитера (тогда он лезет в конфиг/БД)?
  · `IWebhookRateLimiter.cs` + `TokenBucketRateLimiter.cs`

## Решения на подходе (нужны для твоих тасок)

- **L1** — обёртка `IWebhookRateLimiter`: `ConcurrentDictionary<Guid,
  TokenBucket>` per-webhook в памяти — ок для MVP? Что меняется при
  втором инстансе API? · `TokenBucketRateLimiter.cs`
- **L3** — `GetOrAdd` создаёт bucket один раз: если у вебхука потом
  поменять `RateLimitPerSecond`, лимитер живёт на старом rate до
  рестарта. MVP-компромисс или чинить (пересоздавать bucket при
  смене rate — а что тогда с токенами)? · `TokenBucketRateLimiter.cs`
- **L4** — индексы БД: `(Status, NextAttemptAt)` → partial на
  две живые статусы? Но stale-scan `Status=InFlight` тогда сядет на
  seq scan. Разбор в PLAN.md → реши после `EXPLAIN` на реальных
  данных. Плюс трюк: `NextAttemptAt = CreatedAt` при insert убирает
  NULL и делает due-scan чистым range-запросом · `WebhookEngineDbContext.cs`
  + `docs/diagrams/schema.sql`

## Защита TASK-005 (делегировано — ревью юзера)

- **S2** — equal jitter vs full: что даёт нижняя граница `d/2`?
  · `BackoffPolicy.cs → ComputeDelay`
- **S3** — почему schedule в кучу после `SaveChanges`, не до?
  · `DeliveryWorker.cs` (блок после SaveChangesAsync)
- **S4** — рестарт убивает кучу — почему ретраи не теряются?
  · `SweeperService.cs` (PG-скан) + `HeapRetryScheduler.cs` (комментарий)
- **S5** — зачем версии записей, если PG и так истина?
  · `HeapRetryScheduler.cs` (`_versions`, lazy deletion)
- **L2** — в `TokenBucket`: стартуем полным или пустым ведром —
  обоснуй свой выбор в коде комментом.
- ~~**S1**~~ ✓ решено версионными записями (2026-10-25): reschedule
  повышает версию, старые записи кучи пропускаются на dequeue; PG
  остаётся истиной — рестарт восстанавливает сканом

## Тренажёр (без единственно верного ответа)

- **T1** ✓ разобрано (дубль приёма vs отправки; ack после send)
- **T2** — может ли получатель видеть «ровно-один-раз», если мы
  шлём at-least-once? Что для этого должен делать он сам?
- **T3** — что хуже для системы: дубль или потеря? У каких
  компонентов у нас выбрано что — и почему это не симметрично?
- **T4** — ставим второй инстанс API за балансером. Какие три
  компонента сломаются первыми и почему?
- **T5** — клиент не получил наш ответ (timeout после того, как PG
  записал). Что видит клиент, что видит система, где дубль?
- **T6** — endpoint получателя флапует (500 → 200 → 500). Сколько
  раз максимум может доставиться одно событие при живом sweeper'е?
- **T7** — чей лимит важнее: наш rate limiter или 429 у получателя?
  Что должно происходить, когда они расходятся?

## Проверки в живую (ждут Docker)

- `docker compose -p webhook-engine up` → predict: два одинаковых
  POST /events → какие коды и тела по порядку?
- LoadGen: `--dup 0.3` → предскажи поведение счётчика dup
