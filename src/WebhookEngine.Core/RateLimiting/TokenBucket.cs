namespace WebhookEngine.Core.RateLimiting;

/// <summary>
/// Token bucket rate limiter. Tokens accrue up to <see cref="Capacity"/>
/// at a constant rate; each send spends one.
/// TASK-004 — hands-on (see docs/guides/task004_walkthrough.md).
/// </summary>
public sealed class TokenBucket
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;

    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucket(double capacity, double refillPerSecond, TimeProvider? time = null)
    {
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _time = time ?? TimeProvider.System;
        // TODO: initial state (full or empty?) + argument validation.
        // See walkthrough — Step 1.
    }

    /// <summary>Attempts to spend one token; true if a send may proceed now.</summary>
    public bool TryTake() => throw new NotImplementedException();

    /// <summary>Time until the next token is available; Zero if one is ready.</summary>
    public TimeSpan TimeUntilAvailable() => throw new NotImplementedException();

    // TODO: private Refill() — lazy top-up, called first inside both public methods.
    // TODO: lock object for concurrent callers (walkthrough — Step 5).
}
