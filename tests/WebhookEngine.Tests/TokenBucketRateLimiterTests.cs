using Microsoft.Extensions.Time.Testing;
using WebhookEngine.Infrastructure.RateLimiting;

namespace WebhookEngine.Tests;

public class TokenBucketRateLimiterTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public async Task Burst_WithinCapacity_NoDelay()
    {
        var limiter = new TokenBucketRateLimiter(new FakeTimeProvider());
        // capacity = rate (D16): 3/sec → first 3 sends with no pause.
        for (var i = 0; i < 3; i++)
            Assert.Equal(TimeSpan.Zero, await limiter.AcquireDelayAsync(A, 3));
    }

    [Fact]
    public async Task OverCapacity_ReturnsWaitTime()
    {
        var limiter = new TokenBucketRateLimiter(new FakeTimeProvider());
        for (var i = 0; i < 2; i++)
            await limiter.AcquireDelayAsync(A, 2);

        var wait = await limiter.AcquireDelayAsync(A, 2);
        // next token at 2/sec — ~0.5s (epsilon for float error)
        Assert.True(wait > TimeSpan.FromSeconds(0.4));
        Assert.True(wait <= TimeSpan.FromSeconds(0.6));
    }

    [Fact]
    public async Task Buckets_PerWebhook_Independent()
    {
        var limiter = new TokenBucketRateLimiter(new FakeTimeProvider());
        await limiter.AcquireDelayAsync(A, 1);          // A exhausted its burst
        Assert.True((await limiter.AcquireDelayAsync(A, 1)) > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, await limiter.AcquireDelayAsync(B, 1)); // B is fresh
    }

    [Fact]
    public async Task Refill_AfterElapsedTime()
    {
        var time = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(time);
        await limiter.AcquireDelayAsync(A, 1);
        Assert.True((await limiter.AcquireDelayAsync(A, 1)) > TimeSpan.Zero);

        time.Advance(TimeSpan.FromSeconds(1.1)); // more than a refill period passed
        Assert.Equal(TimeSpan.Zero, await limiter.AcquireDelayAsync(A, 1));
    }
}
