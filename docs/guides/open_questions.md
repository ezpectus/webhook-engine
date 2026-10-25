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
- **В2** ✓ закрыт эталоном: все три статуса = недоставленная работа;
  потолок только на Pending слеп к наводнению RetryScheduled/InFlight
  (юзер: «не один класс статуса» — верно по сути) · `Endpoints.cs → IngestEvent`
- **В4** ✓ (2026-10-25): страхует `SweeperService` — событие остаётся
  `Pending` в PG, канал лишь ускоритель · `EventQueue.cs` + `SweeperService.cs`
- **В5** ✓ закрыт эталоном: два пути в `SweeperService` — heap
  `ReadDueAsync` (быстрый) + PG-скан `NextAttemptAt <= now` (истина,
  спасает при рестарте) · `SweeperService.cs`
- **В7** ✓ решено данными (2026-10-25, хаос-прогон #7): параллельный
  дубль реален (2 строки attempt#1), `AttemptCount` занижается
  (last-write-wins) — **принято как документированная граница
  at-least-once**: дубль легален, грязный счётчик терпим; попытки
  в аудите честные. Чинка (lease-version) — в scale-волну если
  понадобится · `SweeperService.cs` + `DeliveryWorker.cs`
- **DLQ** ✓ решено данными (хаос-прогон #8): D12 подтверждена живьём —
  обход dedup, `AttemptCount→0`, история сохранена, цикл пошёл заново
- **API-change** ✓ решено: `permitsPerSecond` параметром — лимитер
  не тянет конфиг/БД, остаётся чистым алгоритмом · `TokenBucketRateLimiter.cs`

## Решения на подходе (нужны для твоих тасок)

- **L1** ✓ решено: `ConcurrentDictionary` в памяти — ок для MVP
  (один инстанс). При втором инстансе лимиты расходятся ×2 —
  тогда Redis-лимитер, уже в scale-волне `next.md` · `TokenBucketRateLimiter.cs`
- **L3** ✓ решено: stale rate до рестарта — MVP-компромисс, принят.
  Смена лимита — редкая операция; пересоздание bucket'а — в
  scale-волну вместе с Redis-лимитером · `TokenBucketRateLimiter.cs`
- ~~**L4**~~ ✓ решено данными (2026-10-25, TASK-010): `EXPLAIN
  ANALYZE` на 4806 строк — оба скана едут по индексу; partial ломал
  бы stale-scan, выигрыш ничтожен (80кБ). Полный держим. Детали —
  журнал PLAN.md

## Защита TASK-005 (делегировано — ревью юзера)

*Эталонные ответы записаны — тебе осталось прочитать и сказать
«согласен/не согласен» голосом. Это защита, не бюрократия.*

- **S2** — equal jitter vs full: нижняя граница `d/2` гарантирует,
  что ретрай не случится «слишком рано» — endpoint'у гарантирована
  передышка ≥ половины задержки; full jitter может дать ~0 и добить
  умирающий сервис. При этом разброс [d/2, d) всё ещё размазывает
  стадо. · `BackoffPolicy.cs → ComputeDelay`
- **S3** — schedule после `SaveChanges`, не до: куча — ускоритель,
  PG — истина. Сначала фиксируем `RetryScheduled+NextAttemptAt`
  durable, потом добавляем в быструю кучу. Обратный порядок даёт
  окно «в куче есть, в PG нет» → при крахе событие потеряно для
  скана — хуже, чем «в PG есть, в куче нет» (доберёт тиком). ·
  `DeliveryWorker.cs`
- **S4** — рестарт убивает кучу, но не PG: `NextAttemptAt` —
  persistent. Новый процесс на первом же обходе sweeper'а сканирует
  `Status IN (Pending, RetryScheduled)` и `NextAttemptAt <= now` —
  всё due-работа снова в канале. Проверено живьём: хаос #3, ретраи
  продолжились после kill · `SweeperService.cs`
- **S5** — версии нужны для in-memory корректности *до* PG:
  reschedule бампит версию → старая запись кучи тихо пропускается
  на dequeue. Без версий дубликат в куче выдал бы лишний enqueue
  раньше времени — PG потом разрулит (claim-гард), но это
  бесполезный double-enqueue. Версии — дешёвая фильтрация шума,
  PG — страховка · `HeapRetryScheduler.cs`
- **L2** — в `TokenBucket`: стартуем полным или пустым ведром —
  обоснуй свой выбор в коде комментом.
- ~~**S1**~~ ✓ решено версионными записями (2026-10-25): reschedule
  повышает версию, старые записи кучи пропускаются на dequeue; PG
  остаётся истиной — рестарт восстанавливает сканом

## Найдено в хаос-прогоне 2026-10-25 (факты из done-log таблицы)

- **В8** ✓ решено данными (2026-10-25, хаос-прогон #9): dedup-окно
  бессрочно — unique `IdempotencyKey` не имеет TTL. **Принято:**
  перманентный dedup для MVP безопаснее; если понадобится «то же
  событие через сутки = новое» — ключ получает бакет времени
  (эволюция, не баг) · `Endpoints.cs → IngestEvent`
- **В7-подтверждение** ✓ закрыт выше — воспроизведён и принят как
  граница (см. В7 в ревью-блоке)

## Тренажёр (без единственно верного ответа — по желанию, в любой день)

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

## Проверки в живую — выполнены 2026-10-25

- ✓ два одинаковых POST → `202 accepted` затем `200 duplicate` (тот же
  eventId) — и по Redis, и по PG unique при мёртвом Redis
- ✓ `--dup 0.3` → dup-счётчик ~30%, dedup_hit_ratio 0.315
- Полный живой отчёт: `done-log` строки P-12 и TASK-007 (таблица 9 сбоев)
