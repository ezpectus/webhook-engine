# Changelog — коммиты → файлы → таски

Воркфлоу (AGENTS.md §19): закрытые таски переезжают в
`docs/done-log.md` (вид «по задачам»), сюда падает карта
«коммит → файлы → таск». `tasks.md` — только живой бэклог.

Легенда зон: **[U]** — юзер писал руками · **[D]** — Devin
(платформа/делегировано) · **[docs]** — документы/решения.

---

## 2026-11-09 — E2E-бот + /mode в MockReceiver [D]

- `tests/bot.mjs` — полный цикл живьём (18 проверок): доставка до
  приёмника + HMAC-подпись, дедуп под 10×параллелью, retry на flap,
  DLQ по 500 → replay → Delivered, метрики. ~2 мин, exit 1 на FAIL.
  Запуск: `HOOK_URL=http://mockreceiver:5005 node tests/bot.mjs`.
- `tools/MockReceiver/Program.cs` — `POST /mode?m=X` + `?mode=global`:
  один зарегистрированный вебхук флипается failing↔ok без ре-регистра
  (иначе DLQ→replay→deliver не доказать).
- Находки прогона (ожидания бота, не код): `EventStatus` едет числом
  (Delivered=3, DeadLettered=4); подпись только при заданном `secret`.

## 2026-11-08 — H-2 + H-3: ядерные таски закрыты [U→D delegated]

**Код** (`Endpoints.cs`, `DeliveryWorker.cs`, `SweeperService.cs`,
`IDeduplicationStore.cs`, `RedisDeduplicationStore.cs`,
`WebhookEngineDbContext.cs`, миграция `AddClaimVersionConcurrencyToken`):

- **H-2** — `POST /events` принимает `Idempotency-Key`: header =
  материал ключа (`sha256(wh:key:bucket)`, то же окно). Маркер
  `"<eventId>|<bodyHash>"` в Redis; same key+body → dup, same key +
  иное тело → `409 idempotency_key_reused` на обоих путях
  (Redis-hit + 23505-catch, тело сравнивается семантически —
  `JsonNode.DeepEquals`: jsonb нормализует пробелы, строковое
  сравнение ловилось живьём). >256 → 400. Тесты + 4 проверки в smoke.
- **H-3** — `ClaimVersion` = EF concurrency token; bump на ingest
  claim / replay / sweeper claim / stale reclaim (owner-rollback'и —
  без). Стейл-воркер → `DbUpdateConcurrencyException` → полный
  откат записи, info-лог. Хаос-#7 доказан: лиз 3с → 26 дропов, ноль
  коррупции; лиз 12с → конвергенция `AttemptCount == attempts`.
- **Найден край:** лиз < длительность отправки (~10с) → лайвлок —
  задокументирован в guide/database/PLAN.

**Доки:** `done-log` +2 таски · `tasks.md` карта/тела закрыты ·
`PLAN.md` журнал решений (409 vs 422, кто владеет идемпотентностью,
правило bump'ов, почему версия стережёт запись а не провод) ·
`guide.md` EN+RU — В7 закрыт + лайвлок-край + H-2 контракт ·
`database.md` — ClaimVersion как реальный guard + форматы ключей ·
`architecture.md` — открытые вопросы закрыты · `ru/next.md` — В7
закрыт · `classes.mmd`/`classes.drawio` — контракт стора синкнут.

---

## 2026-11-08 — хвост сверки: drawio, RU-зеркала, known edges [docs]

- `classes.drawio` — IDeduplicationStore синкнут с H-1 контрактом.
- `docs/ru/architecture.md` + `docs/ru/next.md` — баннеры
  «архивный снимок, авторитетны EN» (оба были на pre-H-1 состоянии:
  dedup без bucket-ключей, роадмап до закрытых тасок).
- `guide.md` EN+RU — два known edge: рост словаря корзин лимитера
  (one bucket/webhookId, без эвикции — утечка на миллионах
  вебхуков) и правило N×80 < max_connections для мульти-инстанса.

---

## 2026-11-08 — похожие дыры: lint-скрипт + дрейф архитектуры [D/docs]

- `dashboard/package.json`: `lint` через `.bin`-обёртку падал на
  Windows — `&` в пути репо (`Notification & Webhook Engine`)
  ломает cmd-парсер. Теперь `node node_modules/oxlint/bin/oxlint`
  как у dev/build-скриптов; на CI идентично.
- `docs/architecture.md`: DB-секция — реальные имена таблиц
  (PascalCase), ClaimVersion, UpdatedAt, новый индекс; roadmap —
  честная двухслойная модель backpressure (channel-full → rollback,
  PG depth ≥ PendingThreshold → 503).
- `docs/database.md` — cost-model дополнен слоем 503-check;
  секция backpressure описывает оба слоя.
- `docs/guides/concepts.md`, `h3_walkthrough.md` — snake_case
  имена таблиц → реальные.

---

## 2026-11-08 — алго-аудит + индекс UpdatedAt + диаграммы [D]

Полный проход сложности: ingest O(log N) (B-tree insert+claim),
dedup Redis O(1)/PG O(log N), bucket O(1), heap O(log n),
sweeper-scan O(log N+B), stats O(N) за StatsCache-TTL.
**Найдена дыра:** `GET /events` сортировал по `UpdatedAt` без
индекса → seq scan+sort на каждый полл дашборда.

- Миграция `AddEventUpdatedAtIndex` (+ `HasIndex` в DbContext) —
  применена к живой БД.
- `docs/database.md` — новая секция «Cost model» со всей таблицей
  сложности + индекс в реестре.
- Диаграммы синкнуты: `classes.mmd` — актуальные сигнатуры
  IDeduplicationStore (GetEventIdAsync/MarkAsync(key,eventId)),
  ClaimVersion, bigint-PK, StatsCache, StatusFeedHub; `db.drawio` —
  те же правки (Webhooks/Events имена, bigint PK, nullable,
  Events→DLQ one-to-many); `schema.sql` + `db.mmd` — IDX UpdatedAt.

---

## 2026-11-08 — пул-граница: применён Maximum Pool Size=80 [D]

- `appsettings.json` + `appsettings.Development.json` + compose
  `ConnectionStrings__Postgres` — `Maximum Pool Size=80` во всех
  трёх (dev-файл молча перекрывает базовый — первая правка не
  подействовала, поймано повторным прогоном).
- Проверено под тем же бёрстом ~2500 rps: пул уперся ровно в 80
  (80 idle + 1 active в `pg_stat_activity`), `psql` коннектится,
  `/health` остаётся `ok`. Ранее: сервер 100/100 →
  `FATAL: too many clients` + degraded health.
- `docs/database.md` — граница переведена из «документировано» в
  «исправлено и проверено».

---

## 2026-11-08 — БД-доки + нагрузочный прогон [docs]

LoadGen против живого стека: **300 rps × 15с — 0 ошибок**
(3616 accept + 884 dedup, ratio совпал с конфигом). Бёрст
2500 rps × 12с: **0 серверных исключений** — ~13k «ошибок» были
клиентским исчерпанием сокетов LoadGen'а, API обработал всё дошедшее.
Найдена реальная граница: Npgsql `MaxPoolSize=100` = `max_connections`
PG → под бёрстом пул занял весь сервер (`FATAL: too many clients`
для psql, `/health` → degraded), коннекты висят до IdleLifetime.

- `docs/database.md` — новый док: модель БД по таблицам, зачем
  каждый индекс, измеренное поведение под нагрузкой (пул-граница,
  channel-full→sweeper, порядок отказов).
- `db.mmd` — синкнут с моделью: имена таблиц, `ClaimVersion`,
  bigint-PK attempts/DLQ, nullable-поля. `schema.sql` — bigint
  identity PK, `UpdatedAt` nullable.
- docs/README + README — ссылки на database.md.

---

## 2026-11-08 — доки: compose-сеть в known edges + гигиена docker [docs]

- `docs/guide.md` + `docs/ru/guide.md`: known edge — `localhost`
  внутри контейнера указывает на сам контейнер; приёмник в композе
  адресуется `http://mockreceiver:5005/hook` (`RECEIVER=` в smoke).
  RU-дрейф «В7 → open_questions» синкнут с EN (принято, фикс = H-3).
- `AGENTS.md` (окружение): безопасная гигиена docker — builder/image/
  container prune (~1.9 ГБ вернулось), запрет `volume prune` после
  `compose down` (сносит `pgdata`), compose-DNS факт.

---

## 2026-11-08 — финальный прогон: полный compose + smoke [D/docs]

Полный `docker compose up --build` (postgres + redis + api +
dashboard + mockreceiver): **20/20 smoke-чеков зелёных** на :8080,
end-to-end доставка подтверждена — API-контейнер доставил событие
в mockreceiver по имени сервиса (attempt + запись в приёмнике,
статус Delivered). Дашборд-nginx: `/health`/`/stats`/SignalR
negotiate через WS-upgrade — все 200.

- `RECEIVER`-параметр в `tests/smoke.mjs` — в композе API ходит в
  приёмник по `http://mockreceiver:5005`, локально — по
  `localhost:5005`; регистрируемый URL вебхука теперь
  конфигурируется.
- README: статистика обновлена (~140 файлов, ~9.9k строк),
  compose-вариант smoke задокументирован.

---

## 2026-11-08 — strict TypeScript + lint-чистка дашборда [D]

- `5af2a56` — **[D]** `tsconfig.app.json`: включён `strict` +
  `noUncheckedIndexedAccess` + `noImplicitOverride` и смежные
  (шаблон был урезан — implicit-any компилировался молча; после
  включения — ноль ошибок). `primitives.tsx` разделён: helpers
  `shortId`/`time` → `src/format.ts` (fast-refresh warning).
  `useDashboardData`: стартовый полл через `queueMicrotask` — нет
  синхронного setState в теле effect. Удалён мёртвый скаффолд
  `WebhookEngine.Api.http` (шёл в несуществующий `/weatherforecast`).
  tsc strict: clean · oxlint: 0 warnings.

---

## 2026-11-08 — полный аудит: код + live API + сайт + доки [D/docs]

Живая матрица пройдена целиком: регистрация (4 валидационных 400),
ingest (accept / Redis-dedup тот же id / PG-fallback тот же id +
`eventStatus` при мёртвом Redis), доставка 200/33мс, ретраи
(500/429/timeout — бэкофф виден по меткам), DLQ с причинами, replay
(202 → второй цикл → вторая DLQ-строка + «replayed» аудит), HMAC
`sha256=` доехал до приёмника, /health ok→degraded→ok, /stats+metrics
согласованы. Сайт: скрин+клики puppeteer — карточки, фильтр (3/9),
attempts-панель, DLQ-кнопки, live-фид, чип соединения — всё живое.

- `9a8fbe4` — **[D]** фиксы по итогам аудита: `GET /events/{id}`
  сортирует attempts по `AttemptedAt` (после replay нумерация
  перезапускается — два цикла перемешивались и давали дублирующиеся
  React-ключи в таблице); dashboard fetch-хелперы режут non-2xx до
  парсинга; replay обновляет только при успехе; `StatsCache` получает
  `TimeProvider` из DI; оставшаяся кириллица в src/ → EN (29 строк,
  9 файлов — прошлый sweep был неполным).
- `52feb17` — **[docs]** `docs/ru/guide.md` синкнут с пост-H-1
  реальностью (был pre-H-1 flow + sweeper 10с), EN помечена
  авторитетной; режимы MockReceiver в картах файлов (ok/drop
  добавлены); concepts.md — `/stats` через 3с StatsCache;
  скриншот дашборда переснят с живым фидом и DLQ-аудитом.
- `6e82ae4` — **[docs]** guide + ru/guide — «Known edges» дополнены
  фактами аудита: payload = строка с JSON, auth отсутствует, lenient
  `?status=`, connect-timeout первого ingest'а после смерти Redis;
  CHANGELOG — секция аудита с результатами живой матрицы.
- `6beea5d` — **[D]** Playground-панель в дашборде: кнопкой
  регистрируются 6 демо-вебхуков на хаос-режимы MockReceiver,
  `send 1`/`send 10` стреляют реальными событиями — ретраи, DLQ
  и фид оживают за секунды. Проверено живьём: 10 InFlight +
  RETRYSCHEDULED-каскад в фиде.
- `095822e` — **[D]** `tests/smoke.mjs` — живой смоук-тест без
  зависимостей (node+fetch): 20 проверок всей поверхности API +
  SignalR negotiate + опциональный дашборд. Все зелёные.
- `098db79` — **[docs]** опенсорс-лицо: `CONTRIBUTING.md` (для кого,
  правила границ, верификация, first contributions), README —
  блок цели/аудитории + статистика (~120 файлов, ~9k строк),
  Playground-секция, команда смоука, «чему научил проект».
- `d256b8b` — **[D]** тесты на непокрытые границы: dedup-store
  (8 шт — fail-open, легаси-значения, NX+TTL), StatsCache (5 шт —
  агрегация, TTL по FakeTimeProvider, параллельные вызовы),
  EngineMetrics (2 шт — Interlocked без потерь). Moq + EF InMemory
  в тест-проект. 40 → **56** зелёных. Endpoints/workers осознанно
  не юнитятся (ExecuteUpdateAsync — relational-only, покрыто
  живой матрицей).
- `ceb437b` — **[D]** рефактор дашборда в нормальную структуру React:
  App.tsx-простыня (228 строк) разбита на `components/` (8 файлов) +
  хуки `useDashboardData` (поллинг теперь `Promise.all` параллельно)
  и `useStatusFeed` (+`onclose` → честный офлайн-чип); ещё один
  дубль React-ключа в DLQ (replayed-событие в списке дважды) —
  ключ `eventId+deadLetteredAt`. Вид не изменён — проверено скрином.

Известные ограничения (задокументированы, не баги): payload — строка
с JSON внутри (объект в `payload` отклоняется байндером — 400);
первый ingest после смерти Redis ждёт connect-timeout ~10-20с;
`?status=` мусор = без фильтра (lenient); auth отсутствует (MVP);
метрики in-memory — сбрасываются рестартом.

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
- `9d38eca` — **[D+docs]** навигационный facelift + два UI-бага найдены
  скриншотом: `App.css` никогда не импортировался (дашборд жил на
  чужом boilerplate-стиле — карточки/гриды были мёртвы) и `api.ts`
  типизировал counters PascalCase против реального camelCase JSON
  (3 счётчика молча `undefined`). Новые диаграммы `state-machine.mmd`
  + `sequence.mmd` (вставлены в system.md, все 6 блоков проверены
  mmdc), README ← mermaid-схема + живой скриншот
  `docs/screenshots/dashboard.png`, `dashboard/README.md` переписан
  (был Vite-бойлерплейт), guide.md ← пост-H-1 dedup-путь,
  docs/README ← h2/h3-walkthrough и screenshots.
- `75bae2f` — **[D]** рестайл под «строгую консоль»: левые акцентные
  бары + цветные числа карточек по семантике статуса, метрики —
  ячейки-разделители, плотная сетка, uppercase-лейблы, моноширинные
  цифры везде. Скрин переснят: фид показывает живой retry-цикл
  (`RetryScheduled` ×3 у флапающего эндпоинта). Бонус находка —
  один прогон дал `dedup hits 100%`: повторные payload'ы скрипта
  внутри окна вернули `duplicate`, дедуп доказан самим скриншотом.
- `3c0c365` — **[D]** редизайн дашборда под «ops-панель», а не шаблон:
  статусы — цветные бейджи (были сырые int'ы enum'а!), фильтр по
  статусу, колонка webhook, id копируются кликом, HTTP-коды в
  attempts цветные, шапка с timestamp обновления, честный индикатор
  фида. `api.ts` тип `status: string` → `number` (тип врал). Скриншот
  переснят с живым фидом.

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
