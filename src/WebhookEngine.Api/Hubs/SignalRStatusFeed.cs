using Microsoft.AspNetCore.SignalR;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Api.Hubs;

/// <summary>Платформа: фан-аут статусов в SignalR. Ошибки бродкаста глушим —
/// фид это наблюдаемость, а не граница доставки.</summary>
public sealed class SignalRStatusFeed(IHubContext<StatusFeedHub> hub) : IStatusFeed
{
    public async Task PublishAsync(Guid eventId, string status, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.All.SendAsync("status", new { eventId, status }, ct);
        }
        catch
        {
            // фид упал — доставку не блокируем
        }
    }
}
