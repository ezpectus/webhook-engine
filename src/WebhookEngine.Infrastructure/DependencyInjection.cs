using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Infrastructure.Delivery;
using WebhookEngine.Infrastructure.Persistence;
using WebhookEngine.Infrastructure.Queues;
using WebhookEngine.Infrastructure.Redis;

namespace WebhookEngine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<WebhookEngineDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres")));

        var redis = config.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));

        services.AddSingleton<IDeduplicationStore, RedisDeduplicationStore>();
        services.AddSingleton<IEventQueue, EventQueue>();
        services.AddHttpClient<WebhookSender>(c => c.Timeout = TimeSpan.FromSeconds(10));

        return services;
    }
}
