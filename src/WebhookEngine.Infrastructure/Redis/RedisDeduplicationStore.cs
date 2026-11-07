using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Infrastructure.Redis;

/// <summary>
/// Redis implementation of the dedup cache. Policy is fail-open: when
/// Redis is down the check answers "don't know" and the PG unique index
/// stays the hard dedup boundary.
/// </summary>
public sealed class RedisDeduplicationStore(
    IConnectionMultiplexer redis,
    ILogger<RedisDeduplicationStore> logger) : IDeduplicationStore
{
    private IDatabase Db => redis.GetDatabase();

    public async Task<Guid> GetEventIdAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await Db.StringGetAsync(Prefixed(key));
            
            return Guid.TryParse(value,out var id) ? id : Guid.Empty;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup check — failing open, PG index still guards");
            return Guid.Empty;
        }
    }

    public async Task MarkAsync(string key, Guid eventId, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            await Db.StringSetAsync(Prefixed(key), eventId.ToString(), window, When.NotExists);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup mark — skipping cache warm");
        }
    }

    private static string Prefixed(string key) => $"dedup:{key}";
}
