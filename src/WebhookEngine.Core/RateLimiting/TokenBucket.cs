namespace WebhookEngine.Core.RateLimiting;

/// <summary>
/// Token bucket rate limiter. Tokens accrue up to <c>capacity</c>
/// at a constant rate; each send spends one.
/// Reference implementation (TASK-004 — user version preserved above).
/// </summary>
public sealed class TokenBucket
{
    private const double Epsilon = 1e-9; // tolerance for double drift at the token boundary

    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucket(double capacity, double refillPerSecond, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refillPerSecond);

        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = time ?? TimeProvider.System;

        // Start full: a new webhook gets its burst budget right away —
        // "connected → events flow" doesn't wait for tokens to accrue.
        // An empty bucket would delay the first N events for no gain.
        _tokens = capacity;
        _lastRefill = _time.GetUtcNow();
    }

    /// <summary>Attempts to spend one token; true if a send may proceed now.</summary>
    public bool TryTake()
    {
        lock (_gate)
        {
            Refill();
            if (_tokens >= 1 - Epsilon)
            {
                _tokens = Math.Max(0, _tokens - 1); // clamp: micro-drift can't go negative
                return true;
            }
            return false;
        }
    }

    /// <summary>Time until the next token is available; Zero if one is ready.</summary>
    public TimeSpan TimeUntilAvailable()
    {
        lock (_gate)
        {
            Refill();
            return _tokens >= 1 - Epsilon
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds((1 - _tokens) / _refillPerSecond);
        }
    }

    // Lazy refill: accrue only on access — no timer.
    private void Refill()
    {
        var now = _time.GetUtcNow();
        var elapsed = now - _lastRefill;
        if (elapsed <= TimeSpan.Zero) return;   // same tick or clock moved backwards

        _tokens = Math.Min(_capacity, _tokens + elapsed.TotalSeconds * _refillPerSecond);
        _lastRefill = now;
    }
}





/*
 * ── User's version (TASK-004, hands-on) — kept for comparison ──
 *
 * Review verdict: logic fully correct, equivalent to the working version.
 * Real decisions made right: start-full bucket, early exit on
 * elapsed <= 0, Math.Min(capacity), Refill inside the lock.
 *
 * Bugs found during write-up:
 *   • stub methods (throw NotImplementedException) left alongside the
 *     real ones → duplicate signatures, wouldn't compile (CS0111)
 *   • strict `>= 1` on a double → 0.9999999999 drift → false rejection;
 *     fix — Epsilon + Math.Max(0, ...) clamp (Gemini's remark was right,
 *     but the cure isn't decimal: fractional seconds aren't
 *     representable exactly)
 *
public sealed class TokenBucketUserVersion
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;
    private readonly object _lock = new object(); // For thread safety
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucketUserVersion(double capacity, double refillPerSecond, TimeProvider? time = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(refillPerSecond));

        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = time ?? TimeProvider.System;

        _tokens = _capacity;
        _lastRefill = _time.GetUtcNow(); // Initialize to current time
    }

    private void Refill()
    {
        DateTimeOffset now = _time.GetUtcNow();
        TimeSpan elapsed = now - _lastRefill;
        if (elapsed <= TimeSpan.Zero) return;

        _tokens = Math.Min(_capacity, _tokens + elapsed.TotalSeconds * _refillPerSecond);
        _lastRefill = now;
    }

    public bool TryTake()
    {
        lock (_lock)
        {
            Refill();
            if (_tokens >= 1)
            {
                _tokens -= 1;
                return true;
            }
            return false;
        }
    }

    public TimeSpan TimeUntilAvailable()
    {
        lock (_lock)
        {
            Refill();
            if (_tokens >= 1)
            {
                return TimeSpan.Zero;
            }
            return TimeSpan.FromSeconds((1 - _tokens) / _refillPerSecond);
        }
        // TODO: private Refill() — lazy top-up, called first inside both public methods.
        // TODO: lock object for concurrent callers (walkthrough — Step 5).
    }
}
*/


