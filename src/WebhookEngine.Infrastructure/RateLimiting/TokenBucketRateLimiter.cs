using System.Collections.Concurrent;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.RateLimiting;

namespace WebhookEngine.Infrastructure.RateLimiting;

/// <summary>
/// Per-webhook token bucket in memory (D6: MVP is in-process — a
/// second API instance would multiply the limits; then a Redis impl).
/// </summary>
public sealed class TokenBucketRateLimiter(TimeProvider? time = null) : IWebhookRateLimiter
{
    private readonly ConcurrentDictionary<Guid, TokenBucket> _buckets = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<TimeSpan> AcquireDelayAsync(
        Guid webhookId, double permitsPerSecond, CancellationToken ct = default)
    {
        // D16: capacity = rate — one second of burst.
        var bucket = _buckets.GetOrAdd(
            webhookId, _ => new TokenBucket(permitsPerSecond, permitsPerSecond, _time));
        var delay = bucket.TryTake() ? TimeSpan.Zero : bucket.TimeUntilAvailable();
        return Task.FromResult(delay);
    }
}
