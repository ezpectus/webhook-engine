using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using WebhookEngine.Infrastructure.Redis;

namespace WebhookEngine.Tests;

// Dedup cache semantics: Redis is an accelerator — a miss or a dead
// Redis answers Guid.Empty ("don't know") and lets the PG unique
// index stay the hard boundary. The stored value is the original
// event id so a hit returns it directly.
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
    public async Task Get_ReturnsStoredEventId()
    {
        var (store, db) = Sut();
        var id = Guid.NewGuid();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)id.ToString());

        Assert.Equal(id, await store.GetEventIdAsync("k"));
    }

    [Fact]
    public async Task Get_PrefixedKey()
    {
        var (store, db) = Sut();
        RedisKey seen = default;
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .Callback<RedisKey, CommandFlags>((k, _) => seen = k)
          .ReturnsAsync(RedisValue.Null);

        await store.GetEventIdAsync("abc");
        Assert.Equal("dedup:abc", (string?)seen);
    }

    [Fact]
    public async Task Get_NullValue_IsMiss()
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);

        Assert.Equal(Guid.Empty, await store.GetEventIdAsync("k"));
    }

    [Theory]
    [InlineData("1")]                    // legacy pre-H-1 marker value
    [InlineData("not-a-guid")]           // garbage
    [InlineData("")]                     // empty
    public async Task Get_NonGuidValue_IsMiss(string value)
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)value);

        Assert.Equal(Guid.Empty, await store.GetEventIdAsync("k"));
    }

    [Fact]
    public async Task Get_RedisDown_FailsOpen()
    {
        var (store, db) = Sut();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisException("connection refused"));

        // Must not throw — ingest falls back to the PG unique index.
        Assert.Equal(Guid.Empty, await store.GetEventIdAsync("k"));
    }

    [Fact]
    public async Task Mark_SetsKeyWithTtl_AndNotExists()
    {
        var (store, db) = Sut();
        var id = Guid.NewGuid();
        var window = TimeSpan.FromHours(24);
        db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<When>()))
          .ReturnsAsync(true);

        await store.MarkAsync("k", id, window);

        db.Verify(d => d.StringSetAsync(
            (RedisKey)"dedup:k", (RedisValue)id.ToString(),
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
        await store.MarkAsync("k", Guid.NewGuid(), TimeSpan.FromHours(1));
    }
}
