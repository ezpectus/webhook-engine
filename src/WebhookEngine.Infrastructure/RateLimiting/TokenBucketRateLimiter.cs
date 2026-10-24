using System.Collections.Concurrent;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.RateLimiting;

namespace WebhookEngine.Infrastructure.RateLimiting;

/// <summary>
/// Per-webhook token bucket в памяти (D6: MVP — память, при втором
/// инстансе API лимиты размножатся — тогда Redis-реализация).
/// </summary>
public sealed class TokenBucketRateLimiter(TimeProvider? time = null) : IWebhookRateLimiter
{
    private readonly ConcurrentDictionary<Guid, TokenBucket> _buckets = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<TimeSpan> AcquireDelayAsync(
        Guid webhookId, double permitsPerSecond, CancellationToken ct = default)
    {
        // D16: capacity = rate — одна секунда всплеска.
        var bucket = _buckets.GetOrAdd(
            webhookId, _ => new TokenBucket(permitsPerSecond, permitsPerSecond, _time));
        var delay = bucket.TryTake() ? TimeSpan.Zero : bucket.TimeUntilAvailable();
        return Task.FromResult(delay);
    }
}
