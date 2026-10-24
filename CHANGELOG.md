# Changelog — коммиты → файлы → таски

Воркфлоу (AGENTS.md §19): закрытые таски переезжают в
`docs/done-log.md` (вид «по задачам»), сюда падает карта
«коммит → файлы → таск». `tasks.md` — только живой бэклог.

Легенда зон: **[U]** — юзер писал руками · **[D]** — Devin
(платформа/делегировано) · **[docs]** — документы/решения.

---

## 2026-10-25

### `pending` — TASK-005 retry scheduler (делегировано) [D]
- `Core/RetryScheduling/BackoffPolicy.cs` — NEW: `min(cap, base·2^a)` + equal jitter, `MaxAttempts`
- `Core/RetryScheduling/HeapRetryScheduler.cs` — NEW: PriorityQueue + версионный lazy deletion (S1)
- `DeliveryWorker.cs` — backoff вместо flat-delay; max-attempts → DLQ; schedule в кучу после SaveChanges
- `SweeperService.cs` — heap fast-path `ReadDueAsync` + PG-скан (истина); claim вынесен в `ClaimAndEnqueueAsync`
- `DependencyInjection.cs`, `appsettings.json` (секция `Retry`)
- `RetrySchedulerTests.cs` — 8 кейсов; 40/40
- Таски: TASK-005 → review; вопрос S1 закрыт

## 2026-10-24

### `8d6b34d` — HMAC подпись + receiver в compose [D]
- `WebhookSender.cs` — `SendAsync(..., secret?)`: `X-Webhook-Signature: sha256=<hex>` (D14)
- `DeliveryWorker.cs` — передаёт `webhook.Secret`; `WebhookSenderTests` +2 теста (28/28)
- `tools/MockReceiver/` — лог signature-заголовка, `ASPNETCORE_URLS` вместо localhost (fix для контейнера), `Dockerfile`
- `docker-compose.yml` — сервис `mockreceiver` на :5005
- Таски: D14-этап, P-09 частично

### `47df4b5` — MockReceiver + параллельные воркеры [D]
- `tools/MockReceiver/` — NEW: `POST /hook` + `?mode=500|429|slow|flap|drop`, `GET /received` (полигон хаоса)
- `DeliveryWorker.cs` — `Delivery:WorkerParallelism` (дефолт 4): N читателей канала, каждый элемент одному воркеру
- `appsettings.json`

### `cb17bbd` — воркфлоу-доки + метрики [D+docs]
- `AGENTS.md` — переписан структурно; `docs/README.md` (индекс), `docs/next.md` (роадмап) — NEW
- `Program.cs` — `GET /metrics`; `/health` → readiness: пинг PG+Redis, 503 при деградации
- `Core/Metrics/EngineMetrics.cs` — счётчики dedup/send/limiter/reclaim
- `Endpoints.cs`, `DeliveryWorker.cs`, `SweeperService.cs` — точки инкремента

### `0829b37` — race fix + лимитер врезка + hash ring [D]
- `Endpoints.cs` — атомарный claim `Pending→InFlight` (гонка В3)
- `SweeperService.cs` — тот же claim в due-scan + откат при полном канале
- `DeliveryWorker.cs` — rate limiter: `wait>0` → `RetryScheduled` (D7)
- `IWebhookRateLimiter.cs` — сигнатура `+permitsPerSecond` (на ревью)
- `RateLimiting/TokenBucketRateLimiter.cs` — NEW (ConcurrentDictionary, D6)
- `Core/Hashing/ConsistentHashRing.cs` + `ConsistentHashRingTests.cs` — NEW (TASK-008, не врезан)
- `DependencyInjection.cs`, `PLAN.md`, `tasks.md`
- Таски: В3-fix, TASK-004-врезка, TASK-008

### `ee8ea8e` — TASK-004 юзер + тесты [U+D]
- `TokenBucket.cs` — версия юзера в `/* */`, референс живой
- `TokenBucketTests.cs` — 6 кейсов на FakeTimeProvider
- Таски: TASK-004 → review

## 2026-10-21/22 — гайды и скелеты [docs]

- `743d15b` — `guides/task005_walkthrough.md`, `guides/open_questions.md`, чистые комменты bucket
- `8cda683` — walkthrough 004: C2-English + русская версия
- `45b2f4b` — `guides/task004_walkthrough.md` (EN)
- `0b80a71` — скелет TokenBucket + `Microsoft.Extensions.TimeProvider.Testing`; D16
- `a5c388c` — метка «руками» на 004/005; скелет; чекпоинты в журнал

## 2026-10-20 — платформа закрыта [D]

- `2495e3c` — `CHANGELOG.md`, AGENTS §19, чистка tasks.md
- `1732e7f` — `tools/LoadGen/` (P-07); `dashboard/Dockerfile`+`nginx.conf`; compose `dashboard`; P-02 закрыт (без IRepository, D15)
- `24c55d6` — `docs/guides/` + `concepts.md` (CORS/CQRS); README sync
- `dfbea8c` — `docs/guide.md` онбординг; architecture/diagrams sync

## 2026-10-19 — DLQ, наблюдаемость, стиль [D+docs]

- `6723984` — `dashboard/` (React+TS, P-06): stats/DLQ-replay/live-фид; `GET /stats`; `docs/task_guide.md`
- `ae66ed0` — `Api/Hubs/` (SignalR фид, P-05); `IStatusFeed`; `WebhookSenderTests` (P-04 done)
- `cf0ef7c` — DLQ endpoints list+replay (TASK-006 делег.); `EventQueueTests`
- `a1478b6` — D15 арх. стиль в `architecture.md`; step-гайды к 004/005

## 2026-10-17 — приём → доставка [D]

- `9da2bec` — `EventQueue`, `DeliveryWorker`, `SweeperService`, `WebhookSender`; `UpdatedAt` миграция; appsettings (TASK-003)
- `cf61d12` — D3: PG-очередь, reject при `Pending` > порога
- `c781b8c` — `Endpoints.cs` ingest-first + `RedisDeduplicationStore` (TASK-002): `23505`→duplicate, fail-open
- `997ab7c` — раунд решений D1–D14 в журнал
- `ac32169` — диаграммы; решения TASK-002 · `51ae715` — dedup TTL 24ч
- `8a740a6` — правила тренажёра (§17–18) · `153056c` — README на EN
- `8cd2c8a` — TASK-001: data flow, карта дубль/потеря, журнал
- `eac4f40` — детализация тикетов + rationale схемы

## 2026-10-16 — каркас [D+docs]

- `5de7629` — solution `src/*` + `tests/*`, EF-схема + Initial миграция, `Dockerfile`, `docker-compose.yml`, `.dockerignore` (P-01, P-02)
- `e42b4e7` — расширенный бэклог `tasks.md` · `540c1ed` — режимы усталости
- `c38659c` — README, `docs/architecture.md`, `PLAN.md`, `.gitignore`
- `4b7370f` — `AGENTS.md`
