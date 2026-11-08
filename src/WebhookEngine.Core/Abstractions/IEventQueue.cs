using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Bounded in-memory queue — the fast delivery path, NOT a store.
/// Source of truth is the events table; whatever misses the channel
/// is picked up by the sweeper via Pending/RetryScheduled.
/// </summary>
public interface IEventQueue
{
    /// <summary>false — channel full; the event stays for the sweeper.</summary>
    bool TryEnqueue(NotificationEvent ev);

    IAsyncEnumerable<NotificationEvent> ReadAllAsync(CancellationToken ct);
}
