namespace WebhookEngine.Core.Abstractions;

/// <summary>Пуш изменений статуса событий подписчикам (SignalR).</summary>
public interface IStatusFeed
{
    Task PublishAsync(Guid eventId, string status, CancellationToken ct = default);
}
