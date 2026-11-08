namespace WebhookEngine.Core.Metrics;

/// <summary>
/// In-memory engine counters (TASK-009). Platform: increments at the
/// boundaries, read via /metrics. Signals, not Prometheus accuracy.
/// </summary>
public sealed class EngineMetrics
{
    public long EventsAccepted;
    public long EventsRejected;      // 503 on backlog ceiling / 400 validation
    public long DedupHits;           // answered "duplicate"
    public long SendsOk;
    public long SendsRetryable;
    public long SendsNonRetryable;
    public long LimiterDelays;       // worker deferred by the rate limit
    public long StaleReclaims;       // sweeper reclaimed an expired InFlight lease

    public void DedupHit() => Interlocked.Increment(ref DedupHits);
    public void Accepted() => Interlocked.Increment(ref EventsAccepted);
    public void Rejected() => Interlocked.Increment(ref EventsRejected);
    public void SendOk() => Interlocked.Increment(ref SendsOk);
    public void SendRetryable() => Interlocked.Increment(ref SendsRetryable);
    public void SendNonRetryable() => Interlocked.Increment(ref SendsNonRetryable);
    public void LimiterDelay() => Interlocked.Increment(ref LimiterDelays);
    public void StaleReclaim(long n) => Interlocked.Add(ref StaleReclaims, n);
}
