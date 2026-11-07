using System.Runtime.CompilerServices;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Core.RetryScheduling;

/// <summary>
/// In-memory min-heap of deferred retries. An ACCELERATOR, not the truth:
/// the durable schedule lives in PG (NotificationEvent.NextAttemptAt) —
/// a restart loses the heap, the sweeper rebuilds it from a scan.
/// </summary>
/// <remarks>
/// PriorityQueue has no Remove → cancel/reschedule via versioned entries:
/// every ScheduleAsync bumps the event's version; on dequeue a stale
/// version is silently skipped (lazy deletion, S1).
/// The lock guards heap and versions: ScheduleAsync is called from
/// workers, ReadDueAsync from the sweeper — concurrently.
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

                // Stale entry version — the event was rescheduled; skip it.
                if (!_versions.TryGetValue(head.Id, out var current) || current != head.Version)
                    continue;
                _versions.Remove(head.Id); // free the slot: next version starts from zero again
                id = head.Id;
            }
            yield return id;
        }
        await Task.CompletedTask;
    }
}
