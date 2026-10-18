namespace WebhookEngine.Core.RateLimiting;

/// <summary>
/// TASK-004 — пишется руками (см. AGENTS.md).
/// Token bucket: токены копятся до Capacity и пополняются
/// со скоростью RefillPerSecond.
/// </summary>
public sealed class TokenBucket
{
    public TokenBucket(double capacity, double refillPerSecond)
        => throw new NotImplementedException();

    /// <summary>Взять токен. true — токен был, можно слать сейчас.</summary>
    public bool TryTake() => throw new NotImplementedException();

    /// <summary>Сколько ждать до ближайшего доступного токена.
    /// Zero — токен есть.</summary>
    public TimeSpan TimeUntilAvailable() => throw new NotImplementedException();
}
