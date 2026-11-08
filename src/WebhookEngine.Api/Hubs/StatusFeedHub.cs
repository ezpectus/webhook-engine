using Microsoft.AspNetCore.SignalR;

namespace WebhookEngine.Api.Hubs;

/// <summary>Delivery status hub. Clients listen on the "status" method.</summary>
public sealed class StatusFeedHub : Hub;
