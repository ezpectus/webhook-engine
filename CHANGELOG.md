# Changelog — коммиты → файлы → таски

Воркфлоу (AGENTS.md §19): закрытые таски переезжают в
`docs/done-log.md` (вид «по задачам»), сюда падает карта
«коммит → файлы → таск». `tasks.md` — только живой бэклог.

Легенда зон: **[U]** — юзер писал руками · **[D]** — Devin
(платформа/делегировано) · **[docs]** — документы/решения.

---

## 2026-10-25

### `b634b09` — TASK-010: EXPLAIN на живых данных [docs]
- 4806 строк: due/stale-scan по `IX_Events_Status_NextAttemptAt`;
  L4 закрыт — полный индекс держим; `/stats` seq-scan → scale-волна
- `PLAN.md` журнал ×4, `open_questions` L4✓, `tasks.md`, `done-log`

### `a923cbd` — TASK-007 хаос-прогон: 9 сбоев живьём [docs]
- `done-log.md` — таблица «сбой → факт → вердикт»: PG-down=честный 500;
  Redis-down=PG unique ловит; рестарт=PG-скан спасает; cap=1=sweeper;
  В7 воспроизведён (2 строки attempt#1); dedup-окно бессрочно (В8)
- `open_questions` +В8, В7-подтверждение; `tasks.md` → review

### `12cd3ab` — P-12 живой прогон + фиксы [D+docs]
- LoadGen 3000@50rps dup30% → 2055/946/0err; PG: 1 attempt на событие
- `docker-compose.yml` pg→5433 (конфликт с нативным postgres);
  `appsettings.Development.json`; LoadGen `InvariantCulture` для --dup
- `tasks.md`: TASK-007/010 расписаны, P-03/P-12 обновлены

### `c4f26e6` — карта классов [docs]
- `docs/diagrams/mmd/classes.mmd` + `io/classes.drawio` — классы по слоям

### `55ebef6` `3fc6ebe` — open_questions: оценки + указатели [docs]
- В2~/В4✓/В5~; карта «что где живёт»; файлы к каждому вопросу

### `dad44f3` — `2c9ad6e` — ревью-фикс + сортировка диаграмм [D+docs]
- `DeliveryWorker.cs` — гард `Status != InFlight → skip` stale-элементов
- вопрос В7 открыт (параллельный дубль через медленный канал)
- `docs/diagrams/{mmd,io}/` — подпапки по форматам

### `b8a6a2f` — диаграммы-комплект [docs]
- `system.mmd/.drawio`, `db.mmd/.drawio`, `schema.sql` (с partial-index блоком)

### `a711832` — read-side + доки [D+docs]
- `Endpoints.cs` — `GET /webhooks`, `GET /events` (take/status)
- ER-диаграмма; README API surface; PLAN журнал + L4; tasks 010/011 + P-12

### `83db57a` — fail-open на DI-границе [D]
- `DependencyInjection.cs` — `abortConnect=false`: мёртвый Redis больше
  не роняет ingest на резолве мультиплексора

### `0202132` `af35470` — дашборд: события и attempts [D]
- `App.tsx/api.ts/App.css` — таблица Recent events + клик → история попыток

### `7b98a8d` `24decef` — журналы + событие-деталь [D+docs]
- `GET /events/{id}` с историей attempts; MockReceiver `DELETE /received`
- done-log (P-08…P-11) + PLAN журнал

### `0c85148` `1539c03` — модель разработки + отметки тасок [docs]
- AGENTS: Kanban + XP соло; tasks.md: TASK-009→review, P-09, список сессии

### `8ff1f7b` — тесты лимитера [D]
- `TokenBucketRateLimiterTests.cs` — 4 кейса (32/32); вопрос L3 открыт

### `a8aa8e5` — метрики в дашборд + концепции [D+docs]
- api.ts/App.tsx/App.css — metrics strip; прокси `/metrics` (vite+nginx)
- `concepts.md` — HMAC + playbook прогона

### `633e8e7` — TASK-005 retry scheduler (делегировано) [D]
- `Core/RetryScheduling/BackoffPolicy.cs` — NEW: `min(cap, base·2^a)` + equal jitter, `MaxAttempts`
- `Core/RetryScheduling/HeapRetryScheduler.cs` — NEW: PriorityQueue + версионный lazy deletion (S1)
- `DeliveryWorker.cs` — backoff вместо flat-delay; max-attempts → DLQ; schedule в кучу после SaveChanges
- `SweeperService.cs` — heap fast-path `ReadDueAsync` + PG-скан (истина); claim вынесен в `ClaimAndEnqueueAsync`
- `DependencyInjection.cs`, `appsettings.json` (секция `Retry`)
- `RetrySchedulerTests.cs` — 8 кейсов; 40/40
- Таски: TASK-005 → review; вопрос S1 закрыт

### `8d6b34d` `47df4b5` — HMAC + MockReceiver + воркеры ×4 [D]
- см. записи ниже за 2026-10-24 (относятся к этой же работе)

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
