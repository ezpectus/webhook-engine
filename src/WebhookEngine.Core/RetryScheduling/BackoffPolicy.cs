namespace WebhookEngine.Core.RetryScheduling;

/// <summary>
/// Политика backoff для ретраев (D9): base × 2^attempt, ограниченная cap,
/// плюс equal jitter — гарантированная половина паузы + разброс второй
/// половины. Чистая функция: никакого состояния, никакой инфраструктуры.
/// </summary>
/// <remarks>
/// Equal jitter выбран вместо full: нижняя граница delay/2 не даёт
/// событиям уйти в «ретрай почти сразу», что важно для флапающих
/// endpoint'ов; разброс верхней половины расселяет стада ретраев,
/// чтобы они не ударили получателя одной волной (thundering herd).
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
    /// Задержка до следующей попытки после попытки № <paramref name="attempt"/>
    /// (0-based). min(cap, base·2^attempt) → equal jitter: [d/2, d).
    /// </summary>
    public TimeSpan ComputeDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        // 2^attempt как сдвиг: attempt ≥ 30 переполнил бы int,
        // но к этому моменту exponential давно за cap'ом.
        var exponential = attempt >= 30 ? double.MaxValue
            : _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var capped = Math.Min(_maxDelay.TotalMilliseconds, exponential);

        var half = capped / 2;
        var jittered = half + _random.NextDouble() * half;
        return TimeSpan.FromMilliseconds(jittered);
    }

    /// <summary>Попытки исчерпаны → событие идёт в DLQ, а не в ретрай.</summary>
    public bool IsExhausted(int attemptCount) => attemptCount >= MaxAttempts;
}
