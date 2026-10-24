namespace WebhookEngine.Core.Metrics;

/// <summary>
/// In-memory счётчики движка (TASK-009). Платформа: инкременты на
/// границах, чтение через /metrics. Не точность Prometheus — сигналы.
/// </summary>
public sealed class EngineMetrics
{
    public long EventsAccepted;
    public long EventsRejected;      // 503 по потолку / 400 валидация
    public long DedupHits;           // ответ "duplicate"
    public long SendsOk;
    public long SendsRetryable;
    public long SendsNonRetryable;
    public long LimiterDelays;       // воркер ушёл на паузу по лимиту
    public long StaleReclaims;       // sweeper вернул протухший InFlight

    public void DedupHit() => Interlocked.Increment(ref DedupHits);
    public void Accepted() => Interlocked.Increment(ref EventsAccepted);
    public void Rejected() => Interlocked.Increment(ref EventsRejected);
    public void SendOk() => Interlocked.Increment(ref SendsOk);
    public void SendRetryable() => Interlocked.Increment(ref SendsRetryable);
    public void SendNonRetryable() => Interlocked.Increment(ref SendsNonRetryable);
    public void LimiterDelay() => Interlocked.Increment(ref LimiterDelays);
    public void StaleReclaim(long n) => Interlocked.Add(ref StaleReclaims, n);
}
