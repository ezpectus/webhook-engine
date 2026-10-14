namespace WebhookEngine.Core.Abstractions;

/// <summary>
/// Планировщик отложенных ретраев. Реализация — зона юзера (TASK-005).
/// </summary>
public interface IRetryScheduler
{
    /// <summary>Поставить событие в расписание на указанный момент.</summary>
    Task ScheduleAsync(Guid eventId, DateTimeOffset nextAttemptAt, CancellationToken ct = default);

    /// <summary>Читать события, время которых пришло.</summary>
    IAsyncEnumerable<Guid> ReadDueAsync(DateTimeOffset now, CancellationToken ct = default);
}
