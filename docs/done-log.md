# Done log — закрытые и сданные таски

Архив «по задачам» (AGENTS.md §19). Карта «коммит → файлы» — в
корневом `CHANGELOG.md`. Живой бэклог — `tasks.md`.

| Таск | Что сделано | Коммиты | Статус |
| --- | --- | --- | --- |
| **TASK-004** Token bucket | Bucket юзера в `/* */` + референс; 6 тестов; epsilon-фикс; лимитер врезан в воркер (D7); защита голосом 26.10 (старт-полный/ленивый refill/lock — эталоны в open_questions) | `ee8ea8e` `0829b37` | done |
| **TASK-006** DLQ endpoints | `GET /deadletters`, `POST .../replay` (D12: обход dedup, счётчик→0); семантика подтверждена юзером + живым replay-циклом в хаос-прогоне | `cf0ef7c` | done |
| **TASK-009** Наблюдаемость | `EngineMetrics` + `/metrics` + панель; выбор юзера 26.10: `dlq_live`, `staleReclaims`, `dedupHits`; эталон — `queue_depth`↑ при плоском `sendsOk` для молчаливой потери | `47df4b5` | done |
| **TASK-008** Consistent hashing | `ConsistentHashRing` + 4 теста (релокация ~1/n); ревью делегировано Devin 26.10: корректно; нитпик — GetNode линейный O(точек), in-flight при +нода страхует claim-гард; не врезан — scale-волна | `0829b37` | done |
| **P-01** Scaffold | solution, DI, `/health` | `5de7629` | done |
| **P-02** Schema+migrations | EF-схема, миграции Initial+UpdatedAt; без IRepository (D15) | `5de7629` `9da2bec` | done |
| **P-03** docker-compose | postgres+redis+api+dashboard(nginx)+mockreceiver. Полный прогон 2026-10-26: все 5 сервисов в compose, event → mockreceiver по внутреннему DNS с HMAC, nginx проксирует /stats /metrics /events /webhooks /deadletters /health /hubs; LoadGen 1000@100rps → 804 Delivered, 0 err; порт БД 5433 (нативный pg на 5432) | `5de7629` `1732e7f` | done |
| **P-04** WebhookSender | HttpClientFactory, классификация, 11 тестов | `9da2bec` `ae66ed0` | done |
| **P-05** SignalR фид | `/hubs/status`, метод `"status"`, паблиш из воркера | `ae66ed0` | done |
| **P-06** Dashboard | React+TS: статусы, DLQ+replay, live-фид; vite-прокси | `6723984` `1732e7f` | done |
| **P-07** LoadGen | `tools/LoadGen` (rps/burst/dup счётчики) | `1732e7f` | done |
| **P-08** Метрики + readiness | `EngineMetrics` счётчики на границах; `GET /metrics` (ratios+depth+dlq); `/health` пингует PG+Redis; панель в дашборде | `cb17bbd` `a8aa8e5` | done |
| **P-09** HMAC подпись | `X-Webhook-Signature: sha256=<hex>` по `WebhookEndpoint.Secret` (D14); MockReceiver логирует подпись | `8d6b34d` | done |
| **P-10** MockReceiver | `tools/MockReceiver`: `?mode=ok|500|429|slow|flap|drop`, `GET /received`; compose-сервис :5005 | `47df4b5` `8d6b34d` | done |
| **P-11** Параллельные воркеры | `Delivery:WorkerParallelism` (4): N читателей канала | `47df4b5` | done |
| **TASK-002** Dedup приём | insert-first, `23505`→duplicate, fail-open Redis | `c781b8c` | `review` — чекпоинты частично |
| **TASK-003** Очередь | bounded channel + потолок (D3) + sweeper/lease; гонка В3 починена | `9da2bec` `0829b37` | done |
| **/stats кэш** | `StatsCache` (singleton, TTL 3с, SemaphoreSlim) — `/stats` и `/metrics` делят один снапшот: 4 агрегатных запроса → ≤1 раз в 3с вместо seq-scan на каждый полл дашборда (пункт из scale-волны, TASK-010 находка) | `—` | done |
| **TASK-005** Retry scheduler | `BackoffPolicy` (base·2^a, cap 5мин, equal jitter, max 5) + `HeapRetryScheduler` (версионный lazy deletion, TimeProvider); воркер: ComputeDelay + max→DLQ + schedule после SaveChanges; sweeper: heap fast-path + PG-скан. Защита S1–S5 подтверждена голосом; проверено живьём (хаос #3/#5) | `633e8e7` | done |
| **TASK-010** Индексы на живых данных | EXPLAIN на 4806 строк: due-scan 0.07мс по индексу, stale-scan тоже по нему; L4 решён — полный индекс держим; COUNT живых статусов дешёвый (index-only по малому поддереву); `/stats` seq-scan закрыт StatsCache'ом | `—` | done (делегировано 26.10) |
| **TASK-007** Хаос-прогон | 9 сбоев воспроизведены живьём на compose-стеке, результаты ниже в таблице. Все гарантии держались; найдено 2 реальные границы: тик ретрая = тик sweeper'а; dedup-окно фактически бессрочно (unique PG навсегда — см. D-вопрос) | `—` | done — карта границ в PLAN.md, рассказ юзера = финальная верификация |
| **P-12** Живой прогон | PG+Redis+MockReceiver в compose, API локально :5269. **Результаты:** 3000 отправлено (rps 50, dup 30%) → 2055 accepted, 946 dedup-хитов, 0 err; **PG: 2055 событий = 2055 попыток = ровно 1 каждая, 0 мульти-attempt** — дубли доставки = 0. mode=500: 5 ретраев → DLQ `max attempts (5) exhausted` ✓; ретраи каденсированы тиком sweeper (~10с — backoff<10с сливается, зафиксировано как граница). flap: [500→200] восстановился ✓. Replay: AttemptCount→0, история сохранена, цикл пошёл заново ✓ | `—` | done |

## TASK-007 · Хаос-прогон 2026-10-25 — таблица сбоев и фактов

| # | Сбой | Как ломали | Факт | Гарантия | Вердикт |
|---|------|-----------|------|----------|---------|
| 1 | PG мёртв при ingest | `docker stop postgres` → POST /events | HTTP 500 (не ложное 202); после старта всё работает | не потерять молча | ✅ потеря недопустима → фейлим честно |
| 2 | Redis мёртв при dedup | `docker stop redis` → POST ×2 одинаковых | 1-й accepted, 2-й duplicate (same eventId) — PG unique ловит без Redis | идемпотентность | ✅ dedup держится без Redis |
| 3 | Рестарт API с ретраями в куче | kill `dotnet run` при RetryScheduled | ретраи продолжились после рестарта (attempts 2,3 нового процесса) — PG-скан sweeper'а | heap ≠ истина | ✅ потеря недопустима → PG спасает |
| 4 | Канал переполнен | `Queue__Capacity=1`, 3 POST'а | все 3 Delivered — 2 прошли через sweeper | канал = ускоритель | ✅ backpressure без потерь |
| 5 | Endpoint всегда 500 | `?mode=500` | 5 попыток с backoff → DLQ «max attempts (5) exhausted» | потолок ретраев | ✅ |
| 6 | Endpoint флапает | `?mode=flap` | attempts [500, 200] → Delivered | ретрай лечит транзиенты | ✅ |
| 7 | Lease протухает посреди send | `InFlightTimeoutMinutes=0` + `?mode=slow` | **2 строки attempt#1 за 6с** — параллельный дубль-отправка; attemptCount=2 на 3 реальных send'а (last-write-wins) | В7 — легальная граница at-least-once | ⚠️ дубль допустим, счётчик грязный |
| 8 | Replay из DLQ | `POST /deadletters/{id}/replay` | AttemptCount→0, 5 старых attempts в аудите, цикл пошёл заново | replay = новый бюджет | ✅ |
| 9 | Дубль после окна dedup | `Dedup__WindowHours=0`, POST ×2 | всё равно duplicate — **unique-индекс бессрочен**, TTL чистит только Redis-кэш | окно ≠ горизонт | ⚠️ семантика на ревью (D) |

**Выводы:**
- Потерь нигде не было — все сбои деградировали в «честный отказ» или «восстановление из PG».
- В7 воспроизведён: дубль-отправка реальна при протухшем lease — легально по at-least-once, но `AttemptCount` занижается (last-write-wins). Решение: принять границу или lease-version — вопрос В7 в open_questions.
- Dedup-горизонт = ∞: unique `IdempotencyKey` в PG не имеет TTL; `Dedup:WindowHours` влияет только на Redis-кэш. Если нужно окно по времени — ключ должен содержать бакет времени, иначе дубль = навсегда. Новый вопрос в open_questions.
- Каденс ретраев = max(backoff, Sweeper.IntervalSeconds=10с) — backoff<10с сливается в тик.
