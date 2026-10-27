# Guide — как устроена система

Для себя и для тех, кто подключается. Описывает код **как он есть**,
а не как задумано. Последнее обновление 2026-10-25: полный цикл —
приём, dedup, очередь, параллельные воркеры, rate limiter,
backoff+jitter+heap, DLQ, HMAC, метрики.

## Путь события end-to-end

```
POST /events
  → валидация (payload, webhook существует и активен)
  → dedup-ключ = sha256(webhookId + ":" + payload)
  → Redis IsKnown (быстрый фильтр; лёг → пропускаем, fail-open)
  → backpressure: недоставленных >= PendingThreshold → 503
  → INSERT в PG (unique на IdempotencyKey; 23505 → ответ "duplicate")
  → TryEnqueue в bounded channel
       ├── попал    → Status = InFlight
       └── не попал → остаётся Pending (доберёт sweeper)
  → MarkAsync в Redis (кеш «видели», TTL 24ч)
  → 202 Accepted { eventId }

DeliveryWorker (BackgroundService, ×4 параллельных читателя)
  → читает канал → статус == InFlight? (stale-элементы канала пропускаем)
  → rate limiter: wait > 0 → RetryScheduled + NextAttemptAt (не попытка)
  → WebhookSender.SendAsync (POST + HMAC, timeout 10с)
  → пишет DeliveryAttempt (код, ошибка, длительность)
  → 2xx        → Delivered + DeliveredAt        ← это ack
  → retryable  → AttemptCount ≥ 5 → DeadLettered + DeadLetterItem
               → иначе RetryScheduled + NextAttemptAt =
                 now + min(5мин, 1с·2^a) + equal jitter; в кучу после save
  → non-retry  → DeadLettered + DeadLetterItem(reason)
  → воркер упал посреди → событие висит InFlight → sweeper вернёт

SweeperService (каждые 10с, бережно)
  → InFlight с UpdatedAt старше 5 мин → Pending (lease reclaim)
  → fast path: ReadDueAsync из heap → claim → enqueue
  → PG-скан (истина): Pending/RetryScheduled с NextAttemptAt <= now
    → claim → enqueue
```

## Машина состояний `EventStatus`

| Статус | Значение | Кто ставит |
| --- | --- | --- |
| `Pending` | в PG, ждёт канала или времени | ingest (канал полон), sweeper (reclaim) |
| `InFlight` | в канале / в работе у воркера (lease) | ingest, sweeper |
| `RetryScheduled` | ждёт `NextAttemptAt` | воркер после retryable-фейла |
| `Delivered` | ack: endpoint ответил 2xx | воркер |
| `DeadLettered` | карантин, есть `DeadLetterItem` | воркер |

## Где живут гарантии (карта границ)

- **Дубль на приёме** — `notification_events.IdempotencyKey` unique-индекс
  (`23505` → ответ `duplicate` с исходным id). Redis — только ускоритель.
- **Потеря после принятия** — невозможна: всё недоставленное — строка в PG,
  sweeper добирает всё `Pending`/`RetryScheduled`.
- **Дубль на доставке** — возможен и легален: воркер умер после 2xx,
  но до записи ack → повторная отправка.
- **Потеря в работе** — нет: lease `InFlight` протухает через 5 мин →
  sweeper возвращает в `Pending`.

## Карта файлов

| Файл | Что | Зона |
| --- | --- | --- |
| `Api/Endpoints/Endpoints.cs` | POST/GET /events·/webhooks, /deadletters,<br>replay, /stats /metrics /events/{id};<br>threshold, insert-first, atomic claim | юзер ревьюит |
| `Api/Hubs/` | SignalR-фид статусов (`/hubs/status`, метод `"status"`) | платформа |
| `dashboard/` | React-дашборд: статусы, метрики, события<br>+ attempts, DLQ+replay, live-фид | платформа |
| `tools/LoadGen/` | генератор нагрузки (rps/burst/dup %) | платформа |
| `tools/MockReceiver/` | приёмник с режимами хаоса (500/429/slow/flap) | платформа |
| `Infrastructure/Redis/RedisDeduplicationStore.cs` | dedup-кеш, fail-open (abortConnect=false) | юзер ревьюит |
| `Infrastructure/Queues/EventQueue.cs` | bounded channel 10k | ревью |
| `Api/Workers/DeliveryWorker.cs` | ×4 читателя; send, попытки, backoff→DLQ | ревью |
| `Api/Workers/SweeperService.cs` | lease reclaim + heap fast-path + due scan | ревью |
| `Infrastructure/Delivery/WebhookSender.cs` | HTTP send + HMAC, классификация ошибок | платформа |
| `Core/RateLimiting/TokenBucket.cs` | token bucket (юзер в `/* */` + референс) | review |
| `Infrastructure/RateLimiting/TokenBucketRateLimiter.cs` | ConcurrentDict → bucket per webhook | ревью (L1/L3) |
| `Core/RetryScheduling/BackoffPolicy.cs` | exp·2^a + equal jitter + cap + MaxAttempts | review (S2–S5) |
| `Core/RetryScheduling/HeapRetryScheduler.cs` | PriorityQueue + версии (lazy deletion) | review |
| `Core/Metrics/EngineMetrics.cs` | счётчики границ → /metrics | платформа |
| `Core/Hashing/ConsistentHashRing.cs` | готов, не врезан — scale-волна | review |

## Конфиг (appsettings.json)

Значения **локально-оптимальные для демо/учёбы, не прод-рекомендуемые**
— подобраны так, чтобы ретраи, backpressure и sweeper были видны
живьём за секунды. Прод-направление указано в последнем столбце.

| Ключ | Дефолт | За что | Прод-направление |
| --- | --- | --- | --- |
| `Dedup:WindowHours` | 24 | TTL окна дедупа (Redis) | как у Stripe, ок |
| `Queue:Capacity` | 10000 | размер bounded канала | меньше — кусать раньше |
| `Queue:PendingThreshold` | 10000 | 503 при недоставленных больше порога | растить по SLO |
| `Sweeper:IntervalSeconds` | 2 | период обхода = тик ретраев | 5–15с (нагрузка на БД) |
| `Sweeper:BatchSize` | 500 | батч за обход | по метрикам очереди |
| `Sweeper:InFlightTimeoutMinutes` | 5 | lease-протухание | ≈ max(delivery+retry окно) |
| `Delivery:WorkerParallelism` | 4 | параллельных читателей канала | 8–16 / CPU |
| `Delivery:SenderTimeoutSeconds` | 10 | HTTP-таймаут отправки | 5–30 по SLO рецепиентов |
| `Retry:BaseDelaySeconds` | 1 | база экспоненты | ок |
| `Retry:MaxDelaySeconds` | 300 | cap backoff | ок |
| `Retry:MaxAttempts` | 5 | потолок → DLQ | ок |
| `Stats:CacheTtlSeconds` | 3 | TTL снапшота /stats + /metrics | 5–30 по частоте поллинга |

## Известные края (не баги — предметы разговора)

- ~~Гонка enqueue~~ ✓ починена атомарным claim `Pending→InFlight`
  (`0829b37`)
- **В7 — параллельный дубль через медленный канал**: backlog > 5 мин →
  InFlight протухает → reclaim → второй enqueue → два воркера шлют
  одновременно. Дубль легален; `AttemptCount` теряет инкремент
  (last-write-wins). Чинить lease-version или документировать — вопрос
  в `open_questions.md`
- In-memory лимитер и heap — одноинстансные (scale-волна: Redis +
  leader-election)
- `AttemptCount` не защищён concurrency-токеном — см. В7

## Запуск

```bash
docker compose -p webhook-engine up -d postgres redis
dotnet run --project src/WebhookEngine.Api          # :5269
dotnet run --project tools/MockReceiver             # :5005 приёмник
cd dashboard && npm run dev                          # :5173
# POST /webhooks {"url":"http://localhost:5005/hook?mode=flap","secret":"s3"}
#   → POST /events {"webhookId":"..","payload":"{}"}
# нагрузка:
dotnet run --project tools/LoadGen -- --webhook <guid> --rps 100 --dup 0.2
```
