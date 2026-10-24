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
| **TASK-002** Dedup приём | insert-first, `23505`→duplicate, fail-open Redis | `c781b8c` | `review` — чекпоинты частично |
| **TASK-003** Очередь | bounded channel + потолок (D3) + sweeper/lease; гонка В3 починена | `9da2bec` `0829b37` | `review` |
