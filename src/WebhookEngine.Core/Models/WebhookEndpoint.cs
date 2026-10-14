namespace WebhookEngine.Core.Models;

public sealed class WebhookEndpoint
{
    public Guid Id { get; set; }
    public required string Url { get; set; }
    public string? Secret { get; set; }
    public int RateLimitPerSecond { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public List<NotificationEvent> Events { get; set; } = new();
}
