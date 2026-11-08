# Changelog — коммиты → файлы → таски

Воркфлоу (AGENTS.md §19): закрытые таски переезжают в
`docs/done-log.md` (вид «по задачам»), сюда падает карта
«коммит → файлы → таск». `tasks.md` — только живой бэклог.

Легенда зон: **[U]** — юзер писал руками · **[D]** — Devin
(платформа/делегировано) · **[docs]** — документы/решения.

---

## 2026-11-07 — H-1 написан и проверен e2e [U/D]

- `712d7e9` — **[U]** H-1 ядро: `IDeduplicationStore` →
  `GetEventIdAsync`/`MarkAsync(key, eventId, window)`; Redis-store
  хранит eventId значением (`SET NX`, `TryParse` → miss на
  null/легаси/мусоре — fail-open); `IngestEvent` — `TimeProvider`,
  `double WindowHours` с клампом, bucket-секунды, два ключа
  (content=Redis / bucketed=PG), `ComputeKey(long? bucket)`.
- `5e41d3c` — **[D]** платформа: `TimeProvider` в `WebhookSender`,
  `TokenBucket`, `BackoffPolicy`, `HeapRetryScheduler`,
  `SweeperService`, `StatsCache`, тестах и тулзах; все комментарии
  в исходниках → EN (~100 строк); `appsettings` — коммент
  дедуп-окна починен по смыслу (PG-индекс больше не «навсегда»).
- `d3e10eb` — **[docs]** PLAN.md: журнал реализации + 3
  ревью-находки; tasks.md: H-1 → `review` (e2e окном ~7с пройдена,
  остались checkpoint-ответы).
- E2E-факт: `Dedup__WindowHours=0.002` → POST1 accepted → POST2
  duplicate с тем же id **из Redis** (нет `eventStatus` в ответе —
  быстрый путь) → POST3 через 9с = новое событие; в PG два ряда,
  `IdempotencyKey` разошлись по bucket-суффиксу. 40/40 тестов.
- `893cec4` — **[D]** последние утечки `UtcNow`: `TimeProvider` в
  `DeliveryWorker`, `SweeperService` (включая тик `Task.Delay`),
  регистрацию вебхука и DLQ-replay. В src/ реальных часов больше нет
  (MockReceiver — внешний наблюдатель, осознанно).
- `40443dc` — **[D]** границы эндпоинтов: non-JSON payload → 400
  (был 500 22P02), `WindowHours` NaN/≤0→1 и кап 8760ч (был 500
  `OverflowException` на `TimeSpan.FromHours`), webhook URL только
  http(s), `PendingThreshold ≤ 0` → кламп. Проверено живьём 7 кейсов.
- `88eecbe` — **[docs]** H-1 закрыт в бэклоге: строка в done-log
  (+аннотация к хаос-#9), stub в tasks.md, В8 в open_questions
  «закрыто кодом», next.md — очередь H-2/H-3.
- `40bf043` — **[docs]** синк доков под пост-H-1 реальность:
  `dedup-flow.mmd` (+вставки в system.md и architecture.md),
  `schema.sql` ← `ClaimVersion` + L4-решение, incidents.md ← разбор
  3 ревью-багов H-1, README ← honest-window + статус, PLAN ← журнал.
- `9c47d78` — **[docs]** вопросы закрыты окончательно: checkpoint-ответы
  H-1 вписаны в журнал PLAN.md (straddle-дубль: Redis down + стык
  бакета; «навсегда» = баг контракта окна); строка «TTL истёк» в
  карте границ переписана под новое событие; канонические ответы к
  тренажёру T2/T3/T5/T6/T7; DoD-чекбоксы h1_walkthrough закрыты;
  архивный `docs/ru/PLAN.md` помечен pre-H-1.
- `—` — **[D+docs]** навигационный facelift + два UI-бага найдены
  скриншотом: `App.css` никогда не импортировался (дашборд жил на
  чужом boilerplate-стиле — карточки/гриды были мёртвы) и `api.ts`
  типизировал counters PascalCase против реального camelCase JSON
  (3 счётчика молча `undefined`). Новые диаграммы `state-machine.mmd`
  + `sequence.mmd` (вставлены в system.md, все 6 блоков проверены
  mmdc), README ← mermaid-схема + живой скриншот
  `docs/screenshots/dashboard.png`, `dashboard/README.md` переписан
  (был Vite-бойлерплейт), guide.md ← пост-H-1 dedup-путь,
  docs/README ← h2/h3-walkthrough и screenshots.

## 2026-11-04 — подготовка H-полосы [docs]

- `e2ea2a6` — `h2_walkthrough.md` + `h3_walkthrough.md`: меню
  решений с рекомендациями, write map по файлам/строкам,
  verify-скрипты (дробный `WindowHours` для e2e-окна, дробный
  lease-таймаут + `mode=slow` для хаоса #7). `h1_walkthrough.md` —
  залочен под-вопрос (eventId в Redis-value), write map,
  новые сигнатуры `IDeduplicationStore`. `tasks.md` — карта входа
  на walkthrough-файлы.

## 2026-10-28 — платформа под H-3 + гигиена [D]

### `975d00f` — `ClaimVersion` миграция + StatsCache + .gitattributes
- `NotificationEvent` + `ClaimVersion int` — колонка под H-3
  (lease-versioning); миграция `AddEventClaimVersion` сгенерирована
- `StatsCache` — `delivered` берётся из `byStatus` словаря, -1 запрос
  на рефреш (нит из ревью 26.10)
- `.gitattributes` — нормализация eol, конец CRLF-спаму в диффах
- 40/40

## 2026-10-27 — инциденты и подготовка [docs]

- `b78b4ef` — `docs/guides/h1_walkthrough.md` (путь H-1 без кода);
  `TimeProvider.System` в DI для инжектируемого «now» в эндпоинтах [D+docs]
- `ada0859` — `docs/incidents.md` — разбор ревью-находок с до/после
  кодом (I-1..I-4) [docs]
- `bef7c13` — записи ревью в done-log (таблица находок) + CHANGELOG [docs]

## 2026-10-26 — ревью-фиксы [D]

### `158fdfa` — края из ревью: backoff + replay-ordering
- `DeliveryWorker.cs` — `ComputeDelay(AttemptCount - 1)`: первая
  задержка была `2·base` вместо `base` (off-by-one)
- `Endpoints.cs` — replay: enqueue переставлен ПОСЛЕ commit + откат
  в `Pending` при полном канале (зеркалит ingest-паттерн)
- Комментарии «Было: …» рядом с фиксами; 40/40

### `8265dec` — баги из ревью: dispose + валидация rate
- `WebhookSender.cs` — `using var resp`: недиспознутый ответ держал
  соединение в пуле до GC → исчерпание пула на высоком RPS
- `Endpoints.cs` — `rateLimitPerSecond <= 0` → 400: раньше проходил и
  взрывал `TokenBucket` при каждой доставке (вечный crash-loop)
- Комментарии «Было: …»; 40/40

## 2026-10-26 — ранее в этот день

- `a22d04f` — правки по ревью доков: честные цифры LoadGen, формулировка
  «channel loss», указатель на H-3, юниты H-1, payload-hash в H-2,
  EF-concurrency в H-3; RU-копии → `docs/ru/` [docs]
- `926ae8a` — `tasks.md`: практика руками H-1/H-2/H-3 (dedup-окно,
  Idempotency-Key, lease-version) [docs]
- `14c1d2f` — TASK-011: EN-перевод ядра доков, RU в `docs/guides/ru/` [docs]
- `0f789ec` — правка записи: ответ юзера «бакет» верный [docs]
- `2b98a1b` — TASK-004/006/009 → done, T4 разобран [docs]
- `86e369a` — конфиг локально-оптимальный + SenderTimeout/CacheTtl в
  конфиг; guide таблица «прод-направление» [D+docs]
- `c46500b` — полный 5-сервисный compose-прогон (P-03 ✓), StatsCache
  (TTL 3с), `/metrics` на снапшоте [D]

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
