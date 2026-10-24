namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Rate limiter на отправку в конкретный webhook. Реализация — зона юзера (TASK-004).
/// </summary>
public interface IWebhookRateLimiter
{
    /// <summary>
    /// Сколько ждать до разрешённой отправки. TimeSpan.Zero — можно слать сейчас.
    /// permitsPerSecond — лимит webhook'а (из WebhookEndpoint.RateLimitPerSecond).
    /// </summary>
    Task<TimeSpan> AcquireDelayAsync(Guid webhookId, double permitsPerSecond,
        CancellationToken ct = default);
}
