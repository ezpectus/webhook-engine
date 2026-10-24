# TASK-005 walkthrough — RetryScheduler (EN)

Hands-on task (the "руками" rule holds — hints only from me). Prerequisite:
TASK-004 done. Concepts live in `docs/guides/task_guide.md`.

## What you're building

`Core/Retry/RetryScheduler.cs` — implements `IRetryScheduler`
(`Core/Abstractions/IRetryScheduler.cs`):

- `ScheduleAsync(Guid eventId, DateTimeOffset nextAttemptAt, ct)`
- `IAsyncEnumerable<Guid> ReadDueAsync(DateTimeOffset now, ct)`

Plus a pure delay function (decide where it lives — a static method,
a small `BackoffPolicy` class, or inside the scheduler; argue your pick).

## The two halves

### Half 1 — `ComputeDelay(attempt)`

Pure function, no I/O, trivially testable:

- `delay = min(cap, base * 2^attempt)` — D9: base 1s, cap 5min,
  max attempts 5 (attempt ≥ 5 → no retry, event goes to DLQ)
- equal jitter (D16): `delay/2 + rand * delay/2`
  (`Random.Shared` — never `new Random()` per call)

### Half 2 — the heap

`PriorityQueue<Guid, DateTimeOffset>` — due-time as priority, so the
nearest retry sits on top; dequeue is O(log n).

- `ScheduleAsync` → `Enqueue(eventId, nextAttemptAt)` under `lock`,
  return `Task.CompletedTask` (the interface is async; the in-memory
  impl has nothing to await)
- `ReadDueAsync(now)` → loop: `TryPeek` → priority `> now`? stop →
  else `Dequeue`, `yield return eventId`
- same `TimeProvider` discipline as TASK-004 if you compute due-times
  inside — or accept `now` as the param (the interface already passes
  it, so the scheduler can stay clock-free)

## What you must understand cold

**The heap is an accelerator; Postgres is the truth.** The heap dies
with the process — `NextAttemptAt` in the table does not, and the
sweeper already scans it. Consequences:

- a stale heap entry is *not* a bug — the due-check consults PG anyway
- `PriorityQueue` has no `Remove`: cancellation/rescheduling is lazy —
  a dequeued id whose DB row no longer matches simply gets dropped
- duplicates in the heap (same event scheduled twice) → deduplicate at
  dequeue time or tolerate (the PG claim is the arbiter — pick and argue)

## Tests (write first if you can)

- `ComputeDelay(0)` ≈ 1s… `ComputeDelay(4)` ≤ 5min; growth is
  exponential until capped — all within the jitter corridor
- jitter actually varies: two calls differ, but both stay within
  `[delay/2, delay]`
- enqueue 5 ids with scrambled due-times → `ReadDueAsync` yields them
  in due order
- a future-due entry is *not* returned

## What I'll scrutinise at review

- cap actually applied (`Math.Min`), exponential is `2^attempt`, not
  `attempt * base`
- jitter differs per event (`Random.Shared`, not a shared seed)
- heap↔PG divergence tolerated deliberately, not by accident
- lock around every heap access

## Definition of done

- 4+ tests green
- `DeliveryWorker` swap is sketched (where the flat `retryDelay` dies)
- out loud: why a heap beats a sorted scan, and what breaks if the
  process restarts mid-schedule

---

# Русская версия (кратко)

## Что строишь

`RetryScheduler : IRetryScheduler` в `Core`. Две половины:

1. `ComputeDelay(attempt)` — чистая функция:
   `min(cap 5мин, base 1с × 2^attempt)` + equal jitter
   (`delay/2 + rand × delay/2`, `Random.Shared`)
2. Куча `PriorityQueue<Guid, DateTimeOffset>`:
   `ScheduleAsync` → `Enqueue` под `lock`;
   `ReadDueAsync(now)` → `TryPeek` > now → стоп, иначе `Dequeue` + yield

## Главное понимание

Куча — ускоритель, PG — истина. `PriorityQueue.Remove` нет →
отмена ленивая (выкинуть при сверке с `NextAttemptAt`). Дубли в куче —
терпим или фильтруем на dequeue (PG разрулит). Рестарт теряет кучу —
`NextAttemptAt` в таблице живёт, sweeper добирает.

## Тесты

задержка растёт и упирается в cap · jitter в коридоре `[d/2, d]` и
разнится · due-порядок при перемешанной вставке · будущее не выдаётся

## На ревью смотрю

cap применён · `2^attempt`, не `attempt*base` · jitter разный у
событий · расхождение кучи и PG — осознанное · lock на всех доступах

## Done

4+ теста зелёные · набросок замены flat `retryDelay` в воркере ·
голосом: зачем heap vs скан, что ломается при рестарте
