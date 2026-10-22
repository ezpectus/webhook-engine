# TASK-004 walkthrough — TokenBucket

A hands-on task: this document lays out the detailed path, while the
underlying concepts live in `task_guide.md`. No code below — the
implementation itself is yours to write.

## What you're building

`src/WebhookEngine.Core/RateLimiting/TokenBucket.cs` — a self-contained,
in-memory rate limiter. No Redis, no database, no external dependencies.
The public API is already fixed by the skeleton:

- `TokenBucket(double capacity, double refillPerSecond, TimeProvider? time = null)`
- `bool TryTake()`
- `TimeSpan TimeUntilAvailable()`

Fields declared: `_capacity`, `_refillPerSecond`, `_time`, `_tokens`,
`_lastRefill`.

## Step-by-step

### Step 1 — the constructor

The skeleton already assigns the readonly fields. What remains is a
pair of deliberate design decisions:

- **Validation.** Should `capacity <= 0` or `refillPerSecond <= 0` be
  permitted at all? The orthodox answer is `ArgumentOutOfRangeException` —
  fail fast, since a misconfigured bucket that silently admits or
  rejects everything is far worse than an exception thrown at startup.
- **Initial state.** The orthodox choice is to start FULL
  (`_tokens = capacity`, `_lastRefill = _time.GetUtcNow()`): a
  freshly-minted bucket permits exactly one burst of `capacity` sends.
  Should you choose otherwise, be prepared to argue why.

### Step 2 — a private `Refill()`

Invoked at the top of **both** public methods:

- `now = _time.GetUtcNow()`
- `elapsed = (now - _lastRefill).TotalSeconds`
- if `elapsed <= 0` → return early (same tick, or the clock drifting
  backwards — both plausible, neither fatal)
- `_tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond)`
- `_lastRefill = now`

Why lazy refill rather than a ticking timer: no background thread, no
accumulated drift, and O(1) work performed only when someone actually
inspects the bucket.

### Step 3 — `TryTake()`

- `Refill()`
- `_tokens >= 1` → decrement, return `true`
- otherwise → `false`

### Step 4 — `TimeUntilAvailable()`

- `Refill()`
- `_tokens >= 1` → `TimeSpan.Zero`
- otherwise → `TimeSpan.FromSeconds((1 - _tokens) / _refillPerSecond)`

### Step 5 — thread safety

Guard the refill-and-mutate region with a `lock`. The failure mode
without it: two workers calling `TryTake` concurrently could both
observe `_tokens >= 1`, both decrement, and drive `_tokens` to `-1`.
One shared lock object, both public methods.

### Step 6 — tests (ideally written first)

In `tests/WebhookEngine.Tests/TokenBucketTests.cs`, backed by
`FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing` —
already installed; `provider.Advance(...)` moves the clock without a
single real sleep).

Scenarios worth covering:

- fresh bucket with capacity=5, rate=10 → five consecutive `TryTake`
  calls succeed, the sixth is refused
- after `Advance(100ms)` at rate=10 → precisely one further take succeeds
- `TimeUntilAvailable` on an empty bucket at rate=10 → ≈ 100ms
- `TimeUntilAvailable` on a full bucket → `TimeSpan.Zero`

## What I'll scrutinise at review

- `_tokens` cannot be driven negative under concurrent access
- refill is clamped by `Math.Min(_capacity, ...)`
- no `DateTimeOffset.UtcNow` lurking inside — `TimeProvider` exclusively
- `elapsed <= 0` handled explicitly
- tests rely on `FakeTimeProvider` — zero real `Thread.Sleep`/`Task.Delay`

## Definition of done

- 3+ tests green under `dotnet test`
- you can articulate out loud: why the bucket starts full, why lazy
  refill outperforms a timer, and what the lock is defending
- every line of logic written by you — hints only from me

---

# Русская версия (кратко)

## Что строишь

`TokenBucket` в `Core` — чистый in-memory лимитер. API зафиксирован
скелетом: конструктор `(capacity, refillPerSecond, TimeProvider?)`,
`TryTake()`, `TimeUntilAvailable()`.

## Шаги

1. **Конструктор:** валидация `<= 0` → `ArgumentOutOfRangeException`;
   стартуем ПОЛНЫМ (`_tokens = capacity`) — свежее ведро разрешает
   ровно один всплеск. Будешь иначе — обоснуй.
2. **`Refill()` (private):** зовётся в начале обоих методов:
   `elapsed` секунд с `_lastRefill`; при `<= 0` — ранний выход;
   `_tokens = min(capacity, _tokens + elapsed * rate)`;
   `_lastRefill = now`. Лениво — без таймера и дрейфа.
3. **`TryTake()`:** Refill → `tokens >= 1` → минус один, true; иначе false.
4. **`TimeUntilAvailable()`:** Refill → `tokens >= 1` → `Zero`;
   иначе `(1 - tokens) / rate` секунд.
5. **Потоки:** `lock` на область refill+mutate — иначе два воркера
   загонят `_tokens` в `-1`.
6. **Тесты — первыми:** `FakeTimeProvider`, `Advance(...)` вместо сна.
   Бёрст до capacity → отказ; +100мс → ровно один токен; ожидание
   на пустом ≈ `1/rate`; на полном → `Zero`.

## На ревью смотрю

нет минуса при параллелизме · refill зажат `min(capacity)` ·
`TimeProvider` вместо `UtcNow` · `elapsed <= 0` обработан ·
в тестах ноль реальных слипов

## Done значит

3+ теста зелёные · голосом объяснишь: почему полный старт, почему
ленивый refill, что защищает lock · логика — вся твоя
