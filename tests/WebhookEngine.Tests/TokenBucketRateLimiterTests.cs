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
        // capacity = rate (D16): 3/сек → первые 3 отправки без паузы.
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
        // до следующего жетона при 2/сек — ~0.5с (epsilon на float-ошибку)
        Assert.True(wait > TimeSpan.FromSeconds(0.4));
        Assert.True(wait <= TimeSpan.FromSeconds(0.6));
    }

    [Fact]
    public async Task Buckets_PerWebhook_Independent()
    {
        var limiter = new TokenBucketRateLimiter(new FakeTimeProvider());
        await limiter.AcquireDelayAsync(A, 1);          // A исчерпал burst
        Assert.True((await limiter.AcquireDelayAsync(A, 1)) > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, await limiter.AcquireDelayAsync(B, 1)); // B свежий
    }

    [Fact]
    public async Task Refill_AfterElapsedTime()
    {
        var time = new FakeTimeProvider();
        var limiter = new TokenBucketRateLimiter(time);
        await limiter.AcquireDelayAsync(A, 1);
        Assert.True((await limiter.AcquireDelayAsync(A, 1)) > TimeSpan.Zero);

        time.Advance(TimeSpan.FromSeconds(1.1)); // прошло > периода refill
        Assert.Equal(TimeSpan.Zero, await limiter.AcquireDelayAsync(A, 1));
    }
}
