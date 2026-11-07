using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Api.Workers;

/// <summary>
/// Durability watchdog: returns stale InFlight leases to Pending and
/// feeds the channel events whose time has come. PG is the real queue;
/// the channel is an accelerator.
/// </summary>
public sealed class SweeperService(
    IServiceProvider services,
    IEventQueue queue,
    IRetryScheduler scheduler,
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
        var metrics = scope.ServiceProvider.GetRequiredService<WebhookEngine.Core.Metrics.EngineMetrics>();
        var now = DateTimeOffset.UtcNow;

        // A worker died mid-send — the event hangs in InFlight. Lease expired → back to Pending.
        var staleBefore = now - leaseTimeout;
        var stale = await db.Events
            .Where(e => e.Status == EventStatus.InFlight && e.UpdatedAt < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.Pending)
                .SetProperty(e => e.UpdatedAt, now), ct);
        if (stale > 0)
        {
            metrics.StaleReclaim(stale);
            logger.LogWarning("Reclaimed {Count} stale in-flight events", stale);
        }

        // Fast path: the in-memory heap already knows due items (TASK-005).
        // Every claim is still atomic through PG — the heap is only a hint.
        await foreach (var id in scheduler.ReadDueAsync(now, ct))
            await ClaimAndEnqueueAsync(db, id, now, ct);

        // Slow path (the truth): PG scan — covers restarts and everything
        // the heap lost. The atomic claim resolves overlaps with fast path.
        var due = await db.Events
            .Where(e => (e.Status == EventStatus.Pending || e.Status == EventStatus.RetryScheduled)
                        && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(batch)
            .ToListAsync(ct);

        foreach (var e in due)
            if (!await ClaimAndEnqueueAsync(db, e.Id, now, ct))
                break; // channel full — the next sweep will get it
    }

    private async Task<bool> ClaimAndEnqueueAsync(
        WebhookEngineDbContext db, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        // Atomic claim: only whoever flipped the status does the enqueue.
        var claimed = await db.Events
            .Where(x => x.Id == eventId &&
                        (x.Status == EventStatus.Pending || x.Status == EventStatus.RetryScheduled))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, EventStatus.InFlight)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (claimed == 0)
            return true; // someone else already claimed it — not an error

        var ev = await db.Events.AsNoTracking().FirstAsync(x => x.Id == eventId, ct);
        if (!queue.TryEnqueue(ev))
        {
            // Channel full — roll back to Pending, the next sweep will get it.
            await db.Events
                .Where(x => x.Id == eventId && x.Status == EventStatus.InFlight)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, EventStatus.Pending)
                    .SetProperty(x => x.UpdatedAt, now), ct);
            return false;
        }
        return true;
    }
}
