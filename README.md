# webhook-engine

Real-time сервис доставки уведомлений и webhook'ов с гарантией
at-least-once: идемпотентность, ретраи с backoff, rate limiting,
DLQ и дашборд статуса доставки.

**Стек:** .NET (C#), Redis, PostgreSQL, SignalR, React + TS, docker-compose.

## Документация

- [docs/architecture.md](docs/architecture.md) — идея, компоненты,
  алгоритмы, гарантии, роадмап
- [docs/tasks.md](docs/tasks.md) — бэклог тикетов
- [PLAN.md](PLAN.md) — data flow события и журнал решений
- [AGENTS.md](AGENTS.md) — правила работы над проектом (сплит 50/50)

## Статус

Ранняя стадия — каркас и ядро доставки в работе.
