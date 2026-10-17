namespace WebhookEngine.Core.Models;

public sealed class NotificationEvent
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public WebhookEndpoint Webhook { get; set; } = null!;

    public required string IdempotencyKey { get; set; }
    public required string Payload { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Pending;
    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public List<DeliveryAttempt> Attempts { get; set; } = new();
}
