namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Rate limiter на отправку в конкретный webhook. Реализация — зона юзера (TASK-004).
/// </summary>
public interface IWebhookRateLimiter
{
    /// <summary>
    /// Сколько ждать до разрешённой отправки. TimeSpan.Zero — можно слать сейчас.
    /// </summary>
    Task<TimeSpan> AcquireDelayAsync(Guid webhookId, CancellationToken ct = default);
}
