using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using WebhookEngine.Infrastructure.Redis;

namespace WebhookEngine.Tests;

// Dedup cache semantics: Redis is an accelerator — a miss or a dead
// Redis answers null ("don't know") and lets the PG unique index stay
// the hard boundary. Since H-2 the value is an opaque marker
// "<eventId>|<bodyHash>" — the store passes it through verbatim;
// parsing lives in the endpoint.
public class RedisDeduplicationStoreTests
{
    private static (RedisDeduplicationStore store, Mock<IDatabase> db) Sut()
    {
        var db = new Mock<IDatabase>();
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
           .Returns(db.Object);
        var logger = new Mock<ILogger<RedisDeduplicationStore>>();
        return (new RedisDeduplicationStore(mux.Object, logger.Object), db);
    }

    [Fact]
    public async Task Get_ReturnsRawMarker()
    {
        var (store, db) = Sut();
        var marker = $"{Guid.NewGuid()}|abc123";
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)marker);

        Assert.Equal(marker, await store.GetMarkerAsync("k"));
    }

    [Fact]
    public async Task Get_PrefixedKey()
    {
        var (store, db) = Sut();
        RedisKey seen = default;
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .Callback<RedisKey, CommandFlags>((k, _) => seen = k)
          .ReturnsAsync(RedisValue.Null);

        await store.GetMarkerAsync("abc");
        Assert.Equal("dedup:abc", (string?)seen);
    }

    [Fact]
    public async Task Get_NullValue_IsMiss()
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);

        Assert.Null(await store.GetMarkerAsync("k"));
    }

    [Theory]
    [InlineData("1")]                    // legacy pre-H-2 marker value
    [InlineData("not-a-guid")]           // garbage
    public async Task Get_StoredValue_PassesThroughVerbatim(string value)
    {
        // The store is dumb since H-2: any stored value goes back to the
        // caller, which decides how to parse it.
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)value);

        Assert.Equal(value, await store.GetMarkerAsync("k"));
    }

    [Fact]
    public async Task Get_EmptyValue_IsMiss()
    {
        // StackExchange.Redis normalizes "" to IsNull — an empty marker
        // can't parse to an eventId anyway, so "miss" is the right answer.
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)"");

        Assert.Null(await store.GetMarkerAsync("k"));
    }

    [Fact]
    public async Task Get_RedisDown_FailsOpen()
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisException("connection refused"));

        // Must not throw — ingest falls back to the PG unique index.
        Assert.Null(await store.GetMarkerAsync("k"));
    }

    [Fact]
    public async Task Mark_SetsKeyWithTtl_AndNotExists()
    {
        var (store, db) = Sut();
        var marker = $"{Guid.NewGuid()}|deadbeef";
        var window = TimeSpan.FromHours(24);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
          .ReturnsAsync(true);

        await store.MarkAsync("k", marker, window);

        db.Verify(d => d.StringSetAsync(
            (RedisKey)"dedup:k", (RedisValue)marker,
            window, When.NotExists), Times.Once);
    }

    [Fact]
    public async Task Mark_RedisDown_Swallows()
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
          .ThrowsAsync(new RedisException("connection refused"));

        // Cache warm is best-effort — the row in PG is already durable.
        await store.MarkAsync("k", "id|hash", TimeSpan.FromHours(1));
    }
}
