namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// "Already seen" cache for idempotency keys. NOT the source of truth —
/// the hard dedup boundary is the unique index on IdempotencyKey in PG.
/// Truth order: insert-first (row first, then cache).
/// </summary>
public interface IDeduplicationStore
{
    /// <summary>
    /// Returns the raw marker stored for this key, or null on a miss.
    /// Marker format is "<eventId>|<bodyHash>" (H-2) — the store stays
    /// dumb; callers parse.
    /// </summary>
    Task<string?> GetMarkerAsync(string key, CancellationToken ct = default);

    /// <summary>Marks the key as seen for the dedup window; the value is the pre-formatted marker.</summary>
    Task MarkAsync(string key, string value, TimeSpan window, CancellationToken ct = default);
}
