using Microsoft.AspNetCore.SignalR;

namespace WebhookEngine.Api.Hubs;

/// <summary>Хаб статусов доставки. Клиенты слушают метод "status".</summary>
public sealed class StatusFeedHub : Hub;
