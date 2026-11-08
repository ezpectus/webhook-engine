using Microsoft.AspNetCore.SignalR;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Api.Hubs;

/// <summary>Platform: status fan-out over SignalR. Broadcast errors are
/// swallowed — the feed is observability, not a delivery boundary.</summary>
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
            // feed is down — delivery must not be blocked
        }
    }
}
