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

    // Версия клейма для H-3 (lease-versioning): инкрементится при каждом
    // атомарном claim Pending/RetryScheduled→InFlight. Воркер пишет
    // результат только если его версия ещё актуальна — страховка от
    // last-write-wins при параллельном дубле (В7).
    public int ClaimVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public List<DeliveryAttempt> Attempts { get; set; } = new();
}
