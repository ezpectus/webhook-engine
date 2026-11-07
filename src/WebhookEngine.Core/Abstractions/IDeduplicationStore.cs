namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// "Already seen" cache for idempotency keys. NOT the source of truth —
/// the hard dedup boundary is the unique index on IdempotencyKey in PG.
/// Truth order: insert-first (row first, then cache).
/// </summary>
public interface IDeduplicationStore
{
    /// <summary>Returns the event id stored for this key, or Guid.Empty on a miss.</summary>
    Task<Guid> GetEventIdAsync(string key, CancellationToken ct = default);

    /// <summary>Marks the key as seen for the dedup window; stored value is the eventId (after a successful PG insert).</summary>
    Task MarkAsync(string key, Guid eventId, TimeSpan window, CancellationToken ct = default);
}
