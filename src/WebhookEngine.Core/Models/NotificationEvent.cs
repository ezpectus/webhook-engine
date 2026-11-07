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

    // Lease version (H-3): bumped on every atomic claim
    // Pending/RetryScheduled→InFlight. A worker writes its result only if
    // its version is still current — guards last-write-wins on a
    // concurrent dup (B7).
    public int ClaimVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public List<DeliveryAttempt> Attempts { get; set; } = new();
}
