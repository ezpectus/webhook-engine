using WebhookEngine.Core.RetryScheduling;

namespace WebhookEngine.Tests;

public class BackoffPolicyTests
{
    private static BackoffPolicy Policy(Random? rng = null) =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), 5, rng);

    [Fact]
    public void Delay_GrowsExponentially()
    {
        var p = Policy(new Random(42));
        // attempt 0: base·1, attempt 1: base·2, attempt 2: base·4 — before jitter
        var d0 = p.ComputeDelay(0);
        var d1 = p.ComputeDelay(1);
        var d2 = p.ComputeDelay(2);
        // equal jitter: result falls in [exp/2, exp)
        Assert.InRange(d0.TotalMilliseconds, 500, 1000);
        Assert.InRange(d1.TotalMilliseconds, 1000, 2000);
        Assert.InRange(d2.TotalMilliseconds, 2000, 4000);
    }

    [Fact]
    public void Delay_CappedAtMax()
    {
        var p = Policy(new Random(42));
        for (var i = 0; i < 20; i++)
            Assert.True(p.ComputeDelay(10) <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Jitter_DistributesNotConstant()
    {
        var p = Policy(); // Random.Shared
        var delays = Enumerable.Range(0, 50)
            .Select(_ => p.ComputeDelay(3))
            .ToList();
        // same attempt → different delays — retry herds get scattered
        Assert.True(delays.Distinct().Count() > 10);
    }

    [Fact]
    public void MaxAttempts_IsExhausted()
    {
        var p = Policy();
        Assert.False(p.IsExhausted(4));
        Assert.True(p.IsExhausted(5));
        Assert.True(p.IsExhausted(6));
    }
}

public class HeapRetrySchedulerTests
{
    private static DateTimeOffset T(int seconds) =>
        new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromSeconds(seconds);

    private static async Task<List<Guid>> Drain(HeapRetryScheduler s, DateTimeOffset now)
    {
        var ids = new List<Guid>();
        await foreach (var id in s.ReadDueAsync(now))
            ids.Add(id);
        return ids;
    }

    [Fact]
    public async Task Due_ExtractedInOrder()
    {
        var s = new HeapRetryScheduler();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await s.ScheduleAsync(c, T(30));
        await s.ScheduleAsync(a, T(10));
        await s.ScheduleAsync(b, T(20));

        var due = await Drain(s, T(25));
        Assert.Equal([a, b], due); // c isn't due yet
    }

    [Fact]
    public async Task Future_StaysInHeap()
    {
        var s = new HeapRetryScheduler();
        var a = Guid.NewGuid();
        await s.ScheduleAsync(a, T(60));

        Assert.Empty(await Drain(s, T(30)));
        Assert.Equal([a], await Drain(s, T(60)));
    }

    [Fact]
    public async Task Reschedule_StaleVersionSkipped()
    {
        var s = new HeapRetryScheduler();
        var a = Guid.NewGuid();
        await s.ScheduleAsync(a, T(10));  // v0 — goes stale
        await s.ScheduleAsync(a, T(50));  // v1 — current

        // nothing to pull at T(10): the live entry sits at T(50)
        Assert.Empty(await Drain(s, T(10)));
        Assert.Equal([a], await Drain(s, T(50)));
    }

    [Fact]
    public async Task Concurrent_ScheduleAndRead_NoCrash()
    {
        var s = new HeapRetryScheduler();
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < ids.Length; i++)
                await s.ScheduleAsync(ids[i], T(i % 30));
        });
        var read = Task.Run(async () =>
        {
            var n = 0;
            while (writer.Status == TaskStatus.WaitingForActivation || n < 150)
            {
                await foreach (var _ in s.ReadDueAsync(T(60))) n++;
                if (writer.IsCompleted) break;
            }
        });
        await Task.WhenAll(writer, read);
        var rest = await Drain(s, T(60));
        // the point: no races, no leaked exceptions; the count depends on timing
        Assert.True(rest.Count <= 200);
    }
}
