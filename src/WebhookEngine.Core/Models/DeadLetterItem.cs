namespace WebhookEngine.Core.Models;

public sealed class DeadLetterItem
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public NotificationEvent Event { get; set; } = null!;

    public required string Reason { get; set; }
    public DateTimeOffset DeadLetteredAt { get; set; }
    public DateTimeOffset? ReplayedAt { get; set; }
}
