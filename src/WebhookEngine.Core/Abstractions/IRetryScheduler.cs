namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Scheduler for deferred retries. Implementation is the user's zone (TASK-005).
/// </summary>
public interface IRetryScheduler
{
    /// <summary>Put an event on the schedule for the given moment.</summary>
    Task ScheduleAsync(Guid eventId, DateTimeOffset nextAttemptAt, CancellationToken ct = default);

    /// <summary>Read events whose time has come.</summary>
    IAsyncEnumerable<Guid> ReadDueAsync(DateTimeOffset now, CancellationToken ct = default);
}
