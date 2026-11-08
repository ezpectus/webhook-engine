namespace WebhookEngine.Core.Abstractions;

/// <summary>Pushes event status changes to subscribers (SignalR).</summary>
public interface IStatusFeed
{
    Task PublishAsync(Guid eventId, string status, CancellationToken ct = default);
}
