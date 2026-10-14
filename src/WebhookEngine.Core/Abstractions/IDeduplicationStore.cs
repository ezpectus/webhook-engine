namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Хранилище ключей идемпотентности. Реализация — зона юзера (TASK-002).
/// </summary>
public interface IDeduplicationStore
{
    /// <summary>
    /// Пытается занять ключ на окно дедупликации.
    /// true — ключ новый (событие принимаем), false — уже видели (дубль).
    /// </summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan window, CancellationToken ct = default);
}
