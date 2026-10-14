namespace WebhookEngine.Core.Models;

public enum EventStatus
{
    Pending,
    InFlight,
    RetryScheduled,
    Delivered,
    DeadLettered
}
