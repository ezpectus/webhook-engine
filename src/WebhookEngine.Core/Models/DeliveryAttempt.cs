namespace WebhookEngine.Core.Models;

public sealed class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public NotificationEvent Event { get; set; } = null!;

    public int AttemptNumber { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? Error { get; set; }
    public bool Succeeded { get; set; }
    public int DurationMs { get; set; }
}
