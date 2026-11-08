using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Tests;

// StatsCache: one GROUP BY snapshot serves /stats and /metrics for
// TTL seconds — the dashboard polls every 2s and a fresh seq scan
// per call was the point of TASK-010. Tests pin the aggregation math
// and the TTL window with a fake clock.
public class StatsCacheTests
{
    private static WebhookEngineDbContext Db(string name)
        => new(new DbContextOptionsBuilder<WebhookEngineDbContext>()
            .UseInMemoryDatabase(name).Options);

    private static NotificationEvent Ev(EventStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            WebhookId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Payload = "{}",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static DeadLetterItem Dl(Guid eventId, bool replayed = false)
        => new()
        {
            EventId = eventId,
            Reason = "test",
            DeadLetteredAt = DateTimeOffset.UtcNow,
            ReplayedAt = replayed ? DateTimeOffset.UtcNow : null,
        };

    [Fact]
    public async Task Snapshot_AggregatesStatusesDlqAndDepth()
    {
        var db = Db(nameof(Snapshot_AggregatesStatusesDlqAndDepth));
        var dead = Ev(EventStatus.DeadLettered);
        db.Events.AddRange(
            Ev(EventStatus.Delivered), Ev(EventStatus.Delivered),
            Ev(EventStatus.Pending), dead);
        db.DeadLetters.AddRange(Dl(dead.Id), Dl(Guid.NewGuid(), replayed: true));
        await db.SaveChangesAsync();

        var s = await new StatsCache().GetAsync(db, CancellationToken.None);

        Assert.Equal(2, s.ByStatus[nameof(EventStatus.Delivered)]);
        Assert.Equal(1, s.ByStatus[nameof(EventStatus.Pending)]);
        Assert.Equal(1, s.ByStatus[nameof(EventStatus.DeadLettered)]);
        Assert.Equal(1, s.LiveDeadLetters);   // replayed row excluded
        Assert.Equal(1, s.Depth);             // only Pending counts
        Assert.Equal(2, s.Delivered);
    }

    [Fact]
    public async Task Depth_CountsOnlyUndelivered()
    {
        var db = Db(nameof(Depth_CountsOnlyUndelivered));
        db.Events.AddRange(
            Ev(EventStatus.Delivered), Ev(EventStatus.DeadLettered),
            Ev(EventStatus.Pending), Ev(EventStatus.RetryScheduled),
            Ev(EventStatus.InFlight));
        await db.SaveChangesAsync();

        var s = await new StatsCache().GetAsync(db, CancellationToken.None);

        Assert.Equal(3, s.Depth);       // Pending + RetryScheduled + InFlight
        Assert.Equal(1, s.Delivered);   // read from the same group-by, no extra scan
    }

    [Fact]
    public async Task WithinTtl_ServesCachedSnapshot()
    {
        var db = Db(nameof(WithinTtl_ServesCachedSnapshot));
        var time = new FakeTimeProvider();
        var cache = new StatsCache(time, ttlSeconds: 3);

        db.Events.Add(Ev(EventStatus.Pending));
        await db.SaveChangesAsync();
        var first = await cache.GetAsync(db, CancellationToken.None);

        db.Events.AddRange(Ev(EventStatus.Pending), Ev(EventStatus.Delivered));
        await db.SaveChangesAsync();
        var second = await cache.GetAsync(db, CancellationToken.None);

        Assert.Same(first, second);   // no re-query inside the window
        Assert.Equal(1, second.Depth);
    }

    [Fact]
    public async Task AfterTtl_Refreshes()
    {
        var db = Db(nameof(AfterTtl_Refreshes));
        var time = new FakeTimeProvider();
        var cache = new StatsCache(time, ttlSeconds: 3);

        db.Events.Add(Ev(EventStatus.Pending));
        await db.SaveChangesAsync();
        await cache.GetAsync(db, CancellationToken.None);

        db.Events.AddRange(Ev(EventStatus.Pending), Ev(EventStatus.Delivered));
        await db.SaveChangesAsync();

        time.Advance(TimeSpan.FromSeconds(4));
        var s = await cache.GetAsync(db, CancellationToken.None);

        Assert.Equal(2, s.Depth);
        Assert.Equal(1, s.Delivered);
    }

    [Fact]
    public async Task ConcurrentCallers_AllGetConsistentSnapshot()
    {
        var db = Db(nameof(ConcurrentCallers_AllGetConsistentSnapshot));
        db.Events.AddRange(Ev(EventStatus.Pending), Ev(EventStatus.Delivered));
        await db.SaveChangesAsync();
        var cache = new StatsCache();

        var snaps = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                cache.GetAsync(db, CancellationToken.None)));

        Assert.All(snaps, s => Assert.Equal(1, s.Delivered));
        Assert.All(snaps, s => Assert.Same(snaps[0], s));
    }
}
