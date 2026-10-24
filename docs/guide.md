# Guide — как устроена система

Для себя и для тех, кто подключается. Описывает код **как он есть**,
а не как задумано. Последнее обновление: стадии 1–4 реализованы
(приём, dedup, очередь, доставка, sweeper).

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

DeliveryWorker (BackgroundService)
  → читает канал → находит webhook
  → WebhookSender.SendAsync (POST, timeout 10с)
  → пишет DeliveryAttempt (код, ошибка, длительность)
  → 2xx        → Delivered + DeliveredAt        ← это ack
  → retryable  → RetryScheduled + NextAttemptAt (+30с, заглушка до TASK-005)
  → non-retry  → DeadLettered + DeadLetterItem(reason)
  → воркер упал посреди → событие висит InFlight → sweeper вернёт

SweeperService (каждые 10с, бережно)
  → InFlight с UpdatedAt старше 5 мин → Pending (lease reclaim)
  → Pending/RetryScheduled с NextAttemptAt <= now → InFlight + enqueue
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
| `Api/Endpoints/Endpoints.cs` | POST /events, /webhooks, GET /deadletters,<br>POST replay, GET /stats, GET /metrics;<br>threshold, insert-first, atomic claim | юзер ревьюит |
| `Api/Hubs/` | SignalR-фид статусов (`/hubs/status`, метод `"status"`) | платформа |
| `dashboard/` | React-дашборд: статусы, DLQ+replay, live-фид | платформа |
| `tools/LoadGen/` | генератор нагрузки (rps/burst/dup %) | платформа |
| `Infrastructure/Redis/RedisDeduplicationStore.cs` | dedup-кеш, fail-open | юзер ревьюит |
| `Infrastructure/Queues/EventQueue.cs` | bounded channel 10k | ревью |
| `Api/Workers/DeliveryWorker.cs` | отправка, попытки, статусы | ревью |
| `Api/Workers/SweeperService.cs` | lease reclaim + due scan | ревью |
| `Infrastructure/Delivery/WebhookSender.cs` | HTTP send, классификация ошибок | платформа |
| `Core/RateLimiting/TokenBucket.cs` | **руками юзера — TASK-004** | пустой скелет |

## Конфиг (appsettings.json)

| Ключ | Дефолт | За что |
| --- | --- | --- |
| `Dedup:WindowHours` | 24 | TTL окна дедупа (Redis) |
| `Queue:Capacity` | 10000 | размер bounded канала |
| `Queue:PendingThreshold` | 100000 | 503 при недоставленных больше порога |
| `Sweeper:IntervalSeconds` | 10 | период обхода |
| `Sweeper:BatchSize` | 500 | батч за обход |
| `Sweeper:InFlightTimeoutMinutes` | 5 | lease-протухание |
| `Delivery:RetryDelaySeconds` | 30 | плоская задержка (→ backoff в TASK-005) |

## Известные края (не баги — предметы разговора)

- **Гонка ingest↔sweeper enqueue**: `SaveChanges(Pending)` → `TryEnqueue`
  не атомарны; sweeper может подхватить событие между ними → двойной
  enqueue → дубль доставки. Легально по at-least-once, но лечится
  compare-and-set (`UPDATE ... WHERE Status='Pending'`). — обсудить
- Rate limiter не подключён (TASK-004), backoff — плоский (TASK-005)
- Воркер один последовательный — масштаб читателей канала потом

## Запуск

```bash
docker compose -p webhook-engine up -d postgres redis
dotnet run --project src/WebhookEngine.Api          # :5269
cd dashboard && npm run dev                          # :5173
# POST /webhooks {"url":"https://..."} → POST /events {"webhookId":"..","payload":"{}"}
# нагрузка:
dotnet run --project tools/LoadGen -- --webhook <guid> --rps 100 --dup 0.2
```
