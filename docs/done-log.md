# Done log — закрытые и сданные таски

Архив «по задачам» (AGENTS.md §19). Карта «коммит → файлы» — в
корневом `CHANGELOG.md`. Живой бэклог — `tasks.md`.

| Таск | Что сделано | Коммиты | Статус |
| --- | --- | --- | --- |
| **TASK-004** Token bucket | Bucket юзера в `/* */` + референс; 6 тестов; epsilon-фикс; лимитер врезан в воркер (D7) | `ee8ea8e` `0829b37` | `review` — ждёт голосовое объяснение |
| **TASK-006** DLQ endpoints | `GET /deadletters`, `POST .../replay` (D12: обход dedup, счётчик→0) | `cf0ef7c` | `review` — семантика на ревью |
| **TASK-008** Consistent hashing | `ConsistentHashRing` + 4 теста (релокация ~1/n); в систему не врезан | `0829b37` | `review` |
| **P-01** Scaffold | solution, DI, `/health` | `5de7629` | done |
| **P-02** Schema+migrations | EF-схема, миграции Initial+UpdatedAt; без IRepository (D15) | `5de7629` `9da2bec` | done |
| **P-03** docker-compose | postgres+redis+api+dashboard(nginx) | `5de7629` `1732e7f` | `review` — Docker не поднят |
| **P-04** WebhookSender | HttpClientFactory, классификация, 11 тестов | `9da2bec` `ae66ed0` | done |
| **P-05** SignalR фид | `/hubs/status`, метод `"status"`, паблиш из воркера | `ae66ed0` | `review` — ждёт живого клиента |
| **P-06** Dashboard | React+TS: статусы, DLQ+replay, live-фид; vite-прокси | `6723984` `1732e7f` | `review` |
| **P-07** LoadGen | `tools/LoadGen` (rps/burst/dup счётчики) | `1732e7f` | `review` — ждёт API |
| **P-08** Метрики + readiness | `EngineMetrics` счётчики на границах; `GET /metrics` (ratios+depth+dlq); `/health` пингует PG+Redis; панель в дашборде | `cb17bbd` `a8aa8e5` | done |
| **P-09** HMAC подпись | `X-Webhook-Signature: sha256=<hex>` по `WebhookEndpoint.Secret` (D14); MockReceiver логирует подпись | `8d6b34d` | `review` — ждёт живой приём |
| **P-10** MockReceiver | `tools/MockReceiver`: `?mode=ok|500|429|slow|flap|drop`, `GET /received`; compose-сервис :5005 | `47df4b5` `8d6b34d` | done |
| **P-11** Параллельные воркеры | `Delivery:WorkerParallelism` (4): N читателей канала | `47df4b5` | done |
| **TASK-002** Dedup приём | insert-first, `23505`→duplicate, fail-open Redis | `c781b8c` | `review` — чекпоинты частично |
| **TASK-003** Очередь | bounded channel + потолок (D3) + sweeper/lease; гонка В3 починена | `9da2bec` `0829b37` | `review` |
| **P-12** Живой прогон | PG+Redis+MockReceiver в compose, API локально :5269. **Результаты:** 3000 отправлено (rps 50, dup 30%) → 2055 accepted, 946 dedup-хитов, 0 err; **PG: 2055 событий = 2055 попыток = ровно 1 каждая, 0 мульти-attempt** — дубли доставки = 0. mode=500: 5 ретраев → DLQ `max attempts (5) exhausted` ✓; ретраи каденсированы тиком sweeper (~10с — backoff<10с сливается, зафиксировано как граница). flap: [500→200] восстановился ✓. Replay: AttemptCount→0, история сохранена, цикл пошёл заново ✓ | `—` | done |
