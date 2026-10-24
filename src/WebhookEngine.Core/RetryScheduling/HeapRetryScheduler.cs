using System.Runtime.CompilerServices;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Core.RetryScheduling;

/// <summary>
/// In-memory min-heap отложенных ретраев. УСКОРИТЕЛЬ, не истина:
/// durable-расписание живёт в PG (NotificationEvent.NextAttemptAt) —
/// рестарт теряет кучу, sweeper восстанавливает её из скана.
/// </summary>
/// <remarks>
/// PriorityQueue не умеет Remove → cancel/reschedule через версионные
/// записи: каждый ScheduleAsync повышает версию события, при извлечении
/// устаревшая версия тихо пропускается (lazy deletion, S1).
/// Lock защищает кучу и версии: ScheduleAsync вызывается из воркеров,
/// ReadDueAsync — из sweeper'а, одновременно.
/// </remarks>
public sealed class HeapRetryScheduler : IRetryScheduler
{
    private readonly PriorityQueue<(Guid Id, long Version), DateTimeOffset> _heap = new();
    private readonly Dictionary<Guid, long> _versions = new();
    private readonly object _gate = new();

    public Task ScheduleAsync(Guid eventId, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var version = _versions.TryGetValue(eventId, out var cur) ? cur + 1 : 0;
            _versions[eventId] = version;
            _heap.Enqueue((eventId, version), nextAttemptAt);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Guid> ReadDueAsync(
        DateTimeOffset now,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            Guid id;
            lock (_gate)
            {
                if (!_heap.TryPeek(out var head, out var dueAt) || dueAt > now)
                    yield break;
                _heap.Dequeue();

                // Старая версия записи — событие перепланировали, пропускаем.
                if (!_versions.TryGetValue(head.Id, out var current) || current != head.Version)
                    continue;
                _versions.Remove(head.Id); // слот освобождаем: следующая версия снова с нуля
                id = head.Id;
            }
            yield return id;
        }
        await Task.CompletedTask;
    }
}
