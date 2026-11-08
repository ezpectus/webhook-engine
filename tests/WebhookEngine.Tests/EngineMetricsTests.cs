using WebhookEngine.Core.Metrics;

namespace WebhookEngine.Tests;

// EngineMetrics: Interlocked counters incremented at the delivery
// boundaries. Each public field must move only through its method —
// the /metrics endpoint reads them raw.
public class EngineMetricsTests
{
    [Fact]
    public void EachMethod_IncrementsItsCounter()
    {
        var m = new EngineMetrics();

        m.Accepted();
        m.Rejected();
        m.DedupHit();
        m.SendOk();
        m.SendRetryable();
        m.SendNonRetryable();
        m.LimiterDelay();
        m.StaleReclaim(3);

        Assert.Equal(1, m.EventsAccepted);
        Assert.Equal(1, m.EventsRejected);
        Assert.Equal(1, m.DedupHits);
        Assert.Equal(1, m.SendsOk);
        Assert.Equal(1, m.SendsRetryable);
        Assert.Equal(1, m.SendsNonRetryable);
        Assert.Equal(1, m.LimiterDelays);
        Assert.Equal(3, m.StaleReclaims);
    }

    [Fact]
    public async Task ParallelIncrements_LoseNothing()
    {
        var m = new EngineMetrics();

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                m.Accepted();
                m.DedupHit();
            }
        })));

        Assert.Equal(6400, m.EventsAccepted);
        Assert.Equal(6400, m.DedupHits);
    }
}
