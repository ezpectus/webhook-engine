using WebhookEngine.Core.Models;

namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Bounded in-memory очередь — быстрый путь доставки, НЕ хранилище.
/// Источник истины — таблица events; то, что не попало в канал,
/// добирает sweeper по статусу Pending/RetryScheduled.
/// </summary>
public interface IEventQueue
{
    /// <summary>false — канал полон, событие остаётся для sweeper'а.</summary>
    bool TryEnqueue(NotificationEvent ev);

    IAsyncEnumerable<NotificationEvent> ReadAllAsync(CancellationToken ct);
}
