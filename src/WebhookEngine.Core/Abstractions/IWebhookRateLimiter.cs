namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Rate limiter for sends to a specific webhook. Implementation is the user's zone (TASK-004).
/// </summary>
public interface IWebhookRateLimiter
{
    /// <summary>
    /// How long to wait before a send is permitted. TimeSpan.Zero = send now.
    /// permitsPerSecond = the webhook's limit (WebhookEndpoint.RateLimitPerSecond).
    /// </summary>
    Task<TimeSpan> AcquireDelayAsync(Guid webhookId, double permitsPerSecond,
        CancellationToken ct = default);
}
