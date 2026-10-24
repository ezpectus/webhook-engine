using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using WebhookEngine.Api.Endpoints;
using WebhookEngine.Api.Workers;
using WebhookEngine.Infrastructure;
using WebhookEngine.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddSingleton<WebhookEngine.Core.Abstractions.IStatusFeed,
    WebhookEngine.Api.Hubs.SignalRStatusFeed>();
builder.Services.AddHostedService<DeliveryWorker>();
builder.Services.AddHostedService<SweeperService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/health", async (WebhookEngineDbContext db, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    var pg = await Safe(() => db.Database.CanConnectAsync(ct));
    var rd = await Safe(async () => (await redis.GetDatabase().PingAsync()).TotalMilliseconds >= 0);
    return pg && rd
        ? Results.Ok(new { status = "ok", postgres = true, redis = true })
        : Results.Json(new { status = "degraded", postgres = pg, redis = rd }, statusCode: 503);

    static async Task<bool> Safe(Func<Task<bool>> check)
    { try { return await check(); } catch { return false; } }
});

app.MapEndpoints();
app.MapHub<WebhookEngine.Api.Hubs.StatusFeedHub>("/hubs/status");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WebhookEngineDbContext>();
    db.Database.Migrate();
}

app.Run();
