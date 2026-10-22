# TASK-004 walkthrough — TokenBucket (EN)

Hands-on task. This file is the detailed path; the concepts live in
`task_guide.md`. No code here — the implementation is yours.

## What you're building

`src/WebhookEngine.Core/RateLimiting/TokenBucket.cs` — pure in-memory
rate limiter. No Redis, no DB, no dependencies. The public API is
already fixed by the skeleton:

- `TokenBucket(double capacity, double refillPerSecond, TimeProvider? time = null)`
- `bool TryTake()`
- `TimeSpan TimeUntilAvailable()`

Fields declared: `_capacity`, `_refillPerSecond`, `_time`, `_tokens`,
`_lastRefill`.

## Step-by-step

### Step 1 — constructor

Assign the readonly fields (done in skeleton). Your decisions:

- **Validation:** `capacity <= 0` or `refillPerSecond <= 0` →
  `ArgumentOutOfRangeException`. Fail fast — a broken bucket that
  silently passes or blocks everything is worse than a loud exception.
- **Initial state:** start FULL (`_tokens = capacity`,
  `_lastRefill = _time.GetUtcNow()`). A fresh bucket allows one burst —
  the standard choice. If you pick otherwise, be ready to argue it.

### Step 2 — private `Refill()`

Called at the top of **both** public methods:

- `now = _time.GetUtcNow()`
- `elapsed = (now - _lastRefill).TotalSeconds`
- if `elapsed <= 0` → return early (same tick, or clock drift backwards)
- `_tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond)`
- `_lastRefill = now`

Why lazy refill: no timer thread, no drift, O(1) on demand. The bucket
only "ticks" when someone looks at it.

### Step 3 — `TryTake()`

- `Refill()`
- if `_tokens >= 1` → `_tokens -= 1; return true`
- else → `false`

### Step 4 — `TimeUntilAvailable()`

- `Refill()`
- `_tokens >= 1` → `TimeSpan.Zero`
- else → `TimeSpan.FromSeconds((1 - _tokens) / _refillPerSecond)`

### Step 5 — thread safety

Wrap the refill+mutate section in a `lock`. Reason: two workers calling
`TryTake` simultaneously could both observe `_tokens >= 1` and both
decrement → `_tokens = -1`. One lock object, both public methods.

### Step 6 — tests (write them FIRST if you can)

In `tests/WebhookEngine.Tests/TokenBucketTests.cs`, with
`FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing` —
installed). `provider.Advance(...)` moves the clock without sleeping.

Scenarios:

- fresh bucket, capacity=5, rate=10 → five `TryTake` return true,
  sixth returns false (burst works)
- after `Advance(100ms)` at rate=10 → exactly one more take succeeds
- `TimeUntilAvailable` on an empty bucket at rate=10 → ≈ 100ms
- `TimeUntilAvailable` on a full bucket → `Zero`

## What I'll check at review

- `_tokens` can't go negative under concurrent access
- refill is capped by `Math.Min(_capacity, ...)`
- no `DateTimeOffset.UtcNow` inside — `TimeProvider` only
- `elapsed <= 0` handled
- tests use `FakeTimeProvider`, zero real `Thread.Sleep`/`Task.Delay`

## Definition of done

- 3+ tests green (`dotnet test`)
- you can explain out loud: why the bucket starts full, why lazy
  refill beats a timer, why the lock
- all logic written by you — hints only from me
