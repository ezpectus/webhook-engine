using Microsoft.EntityFrameworkCore;

namespace WebhookEngine.Infrastructure.Persistence;

/// <summary>
/// Short-lived snapshot of aggregate event stats. Dashboard polls /stats and
/// /metrics every 2s; a GROUP BY over the whole Events table per call is a
/// seq scan each time (TASK-010). A few seconds of staleness is fine for
/// dashboards — refresh at most once per TtlSeconds.
/// </summary>
public sealed class StatsCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _ttl;
    private Snapshot? _cached;
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;

    public StatsCache(TimeProvider? time = null, double ttlSeconds = 3)
    {
        _time = time ?? TimeProvider.System;
        _ttl = TimeSpan.FromSeconds(ttlSeconds);
    }

    private readonly TimeProvider _time;

    public sealed record Snapshot(
        Dictionary<string, int> ByStatus,
        int LiveDeadLetters,
        int Depth,
        int Delivered);

    public async Task<Snapshot> GetAsync(WebhookEngineDbContext db, CancellationToken ct)
    {
        if (_cached is not null && _time.GetUtcNow() - _refreshedAt < _ttl)
            return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            // double-check: another caller may have refreshed while we waited
            if (_cached is not null && _time.GetUtcNow() - _refreshedAt < _ttl)
                return _cached;

            var byStatus = await db.Events.AsNoTracking()
                .GroupBy(e => e.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key.ToString(), x => x.Count, ct);
            var dlqLive = await db.DeadLetters.CountAsync(d => d.ReplayedAt == null, ct);
            var depth = await db.Events.CountAsync(
                e => e.Status != Core.Models.EventStatus.Delivered &&
                     e.Status != Core.Models.EventStatus.DeadLettered, ct);
            var delivered = await db.Events.CountAsync(e => e.Status == Core.Models.EventStatus.Delivered, ct);

            _cached = new Snapshot(byStatus, dlqLive, depth, delivered);
            _refreshedAt = _time.GetUtcNow();
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
