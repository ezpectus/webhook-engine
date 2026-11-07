using Microsoft.Extensions.Time.Testing;
using WebhookEngine.Core.RateLimiting;

namespace WebhookEngine.Tests;

public class TokenBucketTests
{
    [Fact]
    public void Burst_AllowsUpToCapacity_ThenRejects()
    {
        var bucket = new TokenBucket(5, 10, new FakeTimeProvider());

        for (var i = 0; i < 5; i++)
            Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void AfterWaiting_TokensRefill()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(5, 10, time);

        for (var i = 0; i < 5; i++)
            bucket.TryTake();

        time.Advance(TimeSpan.FromMilliseconds(200)); // +2 tokens at 10/s

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void TimeUntilAvailable_IsZero_WhenTokensExist()
    {
        var bucket = new TokenBucket(5, 10, new FakeTimeProvider());
        Assert.Equal(TimeSpan.Zero, bucket.TimeUntilAvailable());
    }

    [Fact]
    public void TimeUntilAvailable_OnEmpty_ReturnsWait()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(1, 10, time); // a token drips every 100ms

        Assert.True(bucket.TryTake());

        var wait = bucket.TimeUntilAvailable();
        Assert.True(wait > TimeSpan.Zero && wait <= TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Refill_CappedAtCapacity()
    {
        var time = new FakeTimeProvider();
        var bucket = new TokenBucket(3, 10, time);

        time.Advance(TimeSpan.FromMinutes(5)); // would drip 3000 — but cap is 3

        for (var i = 0; i < 3; i++)
            Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 0)]
    [InlineData(-1, -1)]
    public void Ctor_RejectsNonPositiveArgs(double capacity, double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TokenBucket(capacity, rate));
    }
}
