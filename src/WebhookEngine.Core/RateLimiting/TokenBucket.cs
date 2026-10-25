namespace WebhookEngine.Core.RateLimiting;

/// <summary>
/// Token bucket rate limiter. Tokens accrue up to <c>capacity</c>
/// at a constant rate; each send spends one.
/// Reference implementation (TASK-004 — user version preserved above).
/// </summary>
public sealed class TokenBucket
{
    private const double Epsilon = 1e-9; // допуск на дрейф double на границе токена

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

        // Старт полным: новый webhook сразу получает бюджет на всплеск —
        // «подключил → пошли события» не ждёт накопления токенов.
        // Пустое ведро задержало бы первые N событий без выигрыша.
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
                _tokens = Math.Max(0, _tokens - 1); // клэмп: микродрейф не уводит в минус
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

    // Ленивое пополнение: досчитываем только при обращении — без таймера.
    private void Refill()
    {
        var now = _time.GetUtcNow();
        var elapsed = now - _lastRefill;
        if (elapsed <= TimeSpan.Zero) return;   // один тик или часы пошли назад

        _tokens = Math.Min(_capacity, _tokens + elapsed.TotalSeconds * _refillPerSecond);
        _lastRefill = now;
    }
}





/*
 * ── Версия юзера (TASK-004, hands-on) — сохранена для сравнения ──
 *
 * Вердикт ревью: логика полностью верная, эквивалентна рабочей версии.
 * Реальные решения, сделанные правильно: старт полным ведром, ранний
 * выход на elapsed <= 0, Math.Min(capacity), Refill внутри lock.
 *
 * Баги, найденные при оформлении:
 *   • поверх реализаций остались stub-методы (throw NotImplementedException)
 *     → дубликаты сигнатур, не компилировалось (CS0111)
 *   • строгое `>= 1` на double → дрейф 0.9999999999 → ложный отказ;
 *     фикс — Epsilon + клэмп Math.Max(0, ...) (замечание Gemini верное,
 *     но лекарство — не decimal: дробь секунд непредставима точно)
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


