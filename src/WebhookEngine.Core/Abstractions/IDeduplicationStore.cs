namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Кеш «уже видели» для ключей идемпотентности. НЕ источник истины —
/// жёсткая граница дедупликации это unique-индекс на IdempotencyKey в PG.
/// Истина: insert-first (сначала запись, потом кеш).
/// </summary>
public interface IDeduplicationStore
{
    /// <summary>Быстрая проверка «видели ли ключ». false может значить «новое» или «кеш не знает».</summary>
    Task<bool> IsKnownAsync(string key, CancellationToken ct = default);

    /// <summary>Пометить ключ виденным на окно дедупликации (после успешного insert в PG).</summary>
    Task MarkAsync(string key, TimeSpan window, CancellationToken ct = default);
}
