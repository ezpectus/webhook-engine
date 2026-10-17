using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using WebhookEngine.Core.Abstractions;

namespace WebhookEngine.Infrastructure.Redis;

/// <summary>
/// Redis-реализация dedup-кеша. Политика — fail-open: при недоступном
/// Redis проверка отвечает «не знаю», а дубли отсекает unique-индекс PG.
/// </summary>
public sealed class RedisDeduplicationStore(
    IConnectionMultiplexer redis,
    ILogger<RedisDeduplicationStore> logger) : IDeduplicationStore
{
    private IDatabase Db => redis.GetDatabase();

    public async Task<bool> IsKnownAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await Db.KeyExistsAsync(Prefixed(key));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup check — failing open, PG index still guards");
            return false;
        }
    }

    public async Task MarkAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            await Db.StringSetAsync(Prefixed(key), "1", window, When.NotExists);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis down on dedup mark — skipping cache warm");
        }
    }

    private static string Prefixed(string key) => $"dedup:{key}";
}
