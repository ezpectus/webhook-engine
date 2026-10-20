# Changelog — сделанные таски

Правило (AGENTS.md §19): закрытые таски выгружаются сюда со ссылками
на файлы и коммиты, а из `docs/tasks.md` удаляются — там живёт только
актуальный бэклог. Таски в статусе `review` остаются в tasks.md, пока
юзер не подтвердит ревью — потом переезжают сюда.

---

## 2026-10-20 — платформа закрыта

### P-07 · Генератор нагрузки
- `tools/LoadGen/` — консоль (`--webhook --rps --seconds --burst --dup`),
  счётчики accepted/duplicate/err
- commit `1732e7f`

### P-02 · Postgres: схема + миграции
- `src/WebhookEngine.Infrastructure/Persistence/WebhookEngineDbContext.cs`
- миграции `Migrations/`: Initial + `AddEventUpdatedAt`
- решение: без `IRepository` поверх EF (D15)
- commits `5de7629`, `9da2bec`

### Dashboard в compose
- `dashboard/Dockerfile`, `dashboard/nginx.conf` (прокси + ws → api:8080),
  `docker-compose.yml` сервис `dashboard` на :3000
- commit `1732e7f` — ⚠ не проверено, Docker Desktop не поднят

## 2026-10-19 — DLQ, наблюдаемость, архитектурное решение

### TASK-006 · DLQ endpoints (делегировано, семантика на ревью юзера)
- `Endpoints.cs`: `GET /deadletters`, `POST /deadletters/{id}/replay`
  (D12: обход dedup, AttemptCount→0, история — аудит)
- commit `cf0ef7c`

### P-05 · SignalR status feed
- `Api/Hubs/StatusFeedHub.cs`, `SignalRStatusFeed.cs`,
  `Core/Abstractions/IStatusFeed.cs`; воркер паблишит после SaveChanges
- commit `ae66ed0`

### P-06 · React-дашборд
- `dashboard/` (Vite+React+TS): карточки статусов, DLQ+replay, live-фид;
  `GET /stats`; vite-прокси → :5269
- commit `6723984`

### P-04 · WebhookSender + тесты классификации
- `Infrastructure/Delivery/WebhookSender.cs`;
  `tests/WebhookEngine.Tests/WebhookSenderTests.cs` (11 кейсов)
- commits `9da2bec`, `ae66ed0`

### Документы
- `docs/architecture.md` — раздел «Архитектурный стиль» (D15: модульный
  монолит, не микросервисы/многослойка)
- `docs/guides/task_guide.md`, `docs/guides/concepts.md` (CORS/CQRS)
- `docs/guide.md` — онбординг по системе
- commits `a1478b6`, `dfbea8c`, `24c55d6`

## 2026-10-17 — приём, очередь, доставка

### TASK-002 · Приём + dedup (review закрыт частично)
- `Endpoints.cs` IngestEvent: insert-first, `23505`→duplicate, самолечение
  кеша; `Infrastructure/Redis/RedisDeduplicationStore.cs` — fail-open
- commit `c781b8c`

### TASK-003 · Очередь + backpressure (D3: вариант D с потолком)
- `Infrastructure/Queues/EventQueue.cs` (bounded 10k, TryWrite)
- `Api/Workers/DeliveryWorker.cs`, `Api/Workers/SweeperService.cs`
  (lease `InFlight` + `UpdatedAt`, stale-reclaim 5мин, due-scan 10с)
- `tests/WebhookEngine.Tests/EventQueueTests.cs`
- commits `9da2bec`, `cf0ef7c`

## 2026-10-16 — каркас

### P-01 · Scaffold
- solution `src/WebhookEngine.{Api,Core,Infrastructure}` + `tests/`
- `/health`, DI, `AddInfrastructure`
- `.gitignore`, `Dockerfile`, `.dockerignore`, `docker-compose.yml`
- commits `4b7370f`, `c38659c`, `5de7629`

### Документы проекта
- `AGENTS.md` (правила 50/50), `README.md`, `PLAN.md`,
  `docs/architecture.md`, `docs/tasks.md`, `docs/diagrams/system.md`
