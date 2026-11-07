namespace WebhookEngine.Core.RetryScheduling;

/// <summary>
/// Retry backoff policy (D9): base × 2^attempt, capped, plus equal
/// jitter — a guaranteed half of the pause + a spread on the other half.
/// Pure function: no state, no infrastructure.
/// </summary>
/// <remarks>
/// Equal jitter chosen over full: the delay/2 floor keeps events from
/// "retrying almost immediately", which matters for flapping endpoints;
/// the spread upper half scatters retry herds so they don't hit the
/// receiver as one wave (thundering herd).
/// </remarks>
public sealed class BackoffPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Random _random;

    public int MaxAttempts { get; }

    public BackoffPolicy(TimeSpan baseDelay, TimeSpan maxDelay, int maxAttempts, Random? random = null)
    {
        if (baseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseDelay));
        if (maxDelay < baseDelay)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "max must be >= base");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
        MaxAttempts = maxAttempts;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Delay until the next attempt after attempt № <paramref name="attempt"/>
    /// (0-based). min(cap, base·2^attempt) → equal jitter: [d/2, d).
    /// </summary>
    public TimeSpan ComputeDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        // 2^attempt as a shift: attempt ≥ 30 would overflow int,
        // but by then exponential is way past the cap anyway.
        var exponential = attempt >= 30 ? double.MaxValue
            : _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var capped = Math.Min(_maxDelay.TotalMilliseconds, exponential);

        var half = capped / 2;
        var jittered = half + _random.NextDouble() * half;
        return TimeSpan.FromMilliseconds(jittered);
    }

    /// <summary>Attempts exhausted → the event goes to DLQ, not to retry.</summary>
    public bool IsExhausted(int attemptCount) => attemptCount >= MaxAttempts;
}
