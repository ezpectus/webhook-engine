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
        // abortConnect=false: Connect() не бросает при мёртвом Redis —
        // возвращает мультиплексор в disconnected-режиме (ретраит в фоне).
        // Иначе резолв IConnectionMultiplexer кинул бы исключение и
        // fail-open семантика dedup ломалась бы на DI-границе: Redis down
        // = ingest down, чего быть не должно (PG-индекс — страж).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(
                ConfigurationOptions.Parse($"{redis},abortConnect=false")));

        services.AddSingleton<IDeduplicationStore, RedisDeduplicationStore>();
        services.AddSingleton<IEventQueue, EventQueue>();
        services.AddSingleton<IWebhookRateLimiter, RateLimiting.TokenBucketRateLimiter>();
        services.AddSingleton<Core.Metrics.EngineMetrics>();
        services.AddSingleton(new BackoffPolicy(
            TimeSpan.FromSeconds(config.GetValue("Retry:BaseDelaySeconds", 1)),
            TimeSpan.FromSeconds(config.GetValue("Retry:MaxDelaySeconds", 300)),
            config.GetValue("Retry:MaxAttempts", 5)));
        services.AddSingleton<IRetryScheduler, HeapRetryScheduler>();
        services.AddHttpClient<WebhookSender>(c => c.Timeout = TimeSpan.FromSeconds(10));

        return services;
    }
}
