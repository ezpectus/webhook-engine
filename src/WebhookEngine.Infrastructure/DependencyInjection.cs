using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Infrastructure.Delivery;
using WebhookEngine.Infrastructure.Persistence;
using WebhookEngine.Infrastructure.Queues;
using WebhookEngine.Infrastructure.Redis;
using WebhookEngine.Core.RetryScheduling;

namespace WebhookEngine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<WebhookEngineDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres")));

        var redis = config.GetConnectionString("Redis") ?? "localhost:6379";
        // abortConnect=false: Connect() doesn't throw on a dead Redis —
        // it returns a multiplexer in disconnected mode (retries in the
        // background). Otherwise resolving IConnectionMultiplexer would
        // throw and break dedup's fail-open semantics at the DI boundary:
        // Redis down = ingest down, which must not happen (PG index is
        // the guard).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(
                ConfigurationOptions.Parse($"{redis},abortConnect=false")));

        // Explicit TimeProvider registration: endpoints need an injectable
        // "now" for the testable time bucket in the dedup key (H-1).
        // If the .NET host already registered one — the last registration
        // is the same, harmless.
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IDeduplicationStore, RedisDeduplicationStore>();
        services.AddSingleton<IEventQueue, EventQueue>();
        services.AddSingleton<IWebhookRateLimiter, RateLimiting.TokenBucketRateLimiter>();
        services.AddSingleton<Core.Metrics.EngineMetrics>();
        services.AddSingleton(sp => new Persistence.StatsCache(
            ttlSeconds: config.GetValue("Stats:CacheTtlSeconds", 3),
            time: sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(new BackoffPolicy(
            TimeSpan.FromSeconds(config.GetValue("Retry:BaseDelaySeconds", 1)),
            TimeSpan.FromSeconds(config.GetValue("Retry:MaxDelaySeconds", 300)),
            config.GetValue("Retry:MaxAttempts", 5)));
        services.AddSingleton<IRetryScheduler, HeapRetryScheduler>();
        services.AddHttpClient<WebhookSender>(c => c.Timeout =
            TimeSpan.FromSeconds(config.GetValue("Delivery:SenderTimeoutSeconds", 10)));

        return services;
    }
}
