using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Infrastructure.Redis;

/// <summary>
/// Redis implementation of the dedup cache. Policy is fail-open: when
/// Redis is down the check answers "don't know" (null) and the PG unique
/// index stays the hard dedup boundary. The value is an opaque marker
/// string — parsing lives in the endpoint, not here.
/// </summary>
public sealed class RedisDeduplicationStore(
    IConnectionMultiplexer redis,
    ILogger<RedisDeduplicationStore> logger) : IDeduplicationStore
{
    private IDatabase Db => redis.GetDatabase();

    public async Task<string?> GetMarkerAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await Db.StringGetAsync(Prefixed(key));
            return value.HasValue ? (string?)value : null;
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup check — failing open, PG index still guards");
            return null;
        }
    }

    public async Task MarkAsync(string key, string value, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            await Db.StringSetAsync(Prefixed(key), value, window, When.NotExists);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup mark — skipping cache warm");
        }
    }

    private static string Prefixed(string key) => $"dedup:{key}";
}
