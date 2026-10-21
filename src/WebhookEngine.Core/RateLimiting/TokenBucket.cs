namespace WebhookEngine.Core.RateLimiting;

/// <summary>
/// TASK-004 — пишется руками (см. AGENTS.md, docs/guides/task_guide.md).
/// Token bucket: токены копятся до Capacity и пополняются
/// со скоростью RefillPerSecond.
/// </summary>
public sealed class TokenBucket
{
    // Поля уже объявлены — твоя работа ниже.

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
        // TODO(ты): решить и написать — чему равны _tokens и _lastRefill
        // при создании? Стартуем полным ведром или пустым — и почему?
        // TODO(ты): валидация аргументов — что делать при <= 0?
    }

    /// <summary>Взять токен. true — токен был, можно слать сейчас.</summary>
    /// <remarks>Шаги: пополнить по прошедшему времени → проверить
    /// tokens >= 1 → забрать или вернуть false.</remarks>
    public bool TryTake() => throw new NotImplementedException();

    /// <summary>Сколько ждать до ближайшего доступного токена.
    /// Zero — токен есть.</summary>
    public TimeSpan TimeUntilAvailable() => throw new NotImplementedException();

    // TODO(ты): ленивое пополнение — одна формула из task_guide.
    // TODO(ты): несколько воркеров дёргают один bucket — что защищает
    // _tokens от ухода в минус?
}
