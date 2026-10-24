using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Api.Workers;

/// <summary>
/// Сторож durability: возвращает протухшие InFlight-lease в Pending и
/// подставляет в канал события, время которых пришло. PG — настоящая
/// очередь, канал — ускоритель.
/// </summary>
public sealed class SweeperService(
    IServiceProvider services,
    IEventQueue queue,
    IConfiguration config,
    ILogger<SweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Sweeper:IntervalSeconds", 10));
        var batch = config.GetValue("Sweeper:BatchSize", 500);
        var leaseTimeout = TimeSpan.FromMinutes(config.GetValue("Sweeper:InFlightTimeoutMinutes", 5));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);
            try
            {
                await SweepAsync(batch, leaseTimeout, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sweep failed");
            }
        }
    }

    private async Task SweepAsync(int batch, TimeSpan leaseTimeout, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhookEngineDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Воркер умер посреди send — событие зависло в InFlight. Lease истёк → назад в Pending.
        var staleBefore = now - leaseTimeout;
        var stale = await db.Events
            .Where(e => e.Status == EventStatus.InFlight && e.UpdatedAt < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.Pending)
                .SetProperty(e => e.UpdatedAt, now), ct);
        if (stale > 0)
            logger.LogWarning("Reclaimed {Count} stale in-flight events", stale);

        var due = await db.Events
            .Where(e => (e.Status == EventStatus.Pending || e.Status == EventStatus.RetryScheduled)
                        && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(batch)
            .ToListAsync(ct);

        foreach (var e in due)
        {
            // Атомарный claim: enqueue делает только тот, кто перевёл статус.
            var claimed = await db.Events
                .Where(x => x.Id == e.Id &&
                            (x.Status == EventStatus.Pending || x.Status == EventStatus.RetryScheduled))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, EventStatus.InFlight)
                    .SetProperty(x => x.UpdatedAt, now), ct);
            if (claimed == 0)
                continue; // кто-то другой уже забрал (ingest-claim или конкурент)

            if (!queue.TryEnqueue(e))
            {
                // Канал полон — откат в Pending, следующий обход доберёт.
                await db.Events
                    .Where(x => x.Id == e.Id && x.Status == EventStatus.InFlight)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, EventStatus.Pending)
                        .SetProperty(x => x.UpdatedAt, now), ct);
                break;
            }
            e.Status = EventStatus.InFlight;
            e.UpdatedAt = now;
        }
    }
}
