using System.Diagnostics;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Delivery;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Api.Workers;

public sealed class DeliveryWorker(
    IEventQueue queue,
    IServiceProvider services,
    IConfiguration config,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(config.GetValue("Delivery:RetryDelaySeconds", 30));
        var parallelism = config.GetValue("Delivery:WorkerParallelism", 4);

        // N параллельных читателей канала: каждый элемент достаётся ровно
        // одному воркеру. Упор внешний HTTP, а не CPU — 4 по дефолту.
        var loops = Enumerable.Range(0, parallelism)
            .Select(_ => RunLoopAsync(retryDelay, stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(TimeSpan retryDelay, CancellationToken stoppingToken)
    {
        await foreach (var ev in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WebhookEngineDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<WebhookSender>();
            var feed = scope.ServiceProvider.GetRequiredService<IStatusFeed>();
            var limiter = scope.ServiceProvider.GetRequiredService<IWebhookRateLimiter>();
            var metrics = scope.ServiceProvider.GetRequiredService<WebhookEngine.Core.Metrics.EngineMetrics>();

            try
            {
                await ProcessAsync(ev, db, sender, feed, limiter, metrics, retryDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                // Событие остаётся InFlight — stale-lease в sweeper вернёт его в Pending.
                logger.LogError(ex, "Delivery failed to process event {EventId}", ev.Id);
            }
        }
    }

    private static async Task ProcessAsync(
        NotificationEvent ev,
        WebhookEngineDbContext db,
        WebhookSender sender,
        IStatusFeed feed,
        IWebhookRateLimiter limiter,
        WebhookEngine.Core.Metrics.EngineMetrics metrics,
        TimeSpan retryDelay,
        CancellationToken ct)
    {
        var tracked = await db.Events.FindAsync([ev.Id], ct);
        if (tracked is null)
            return;

        var webhook = await db.Webhooks.FindAsync([ev.WebhookId], ct);

        // D7: задержка по rate limit — тот же механизм отложенной отправки,
        // что и ретрай. Это НЕ попытка: AttemptCount не трогаем, попытку не пишем.
        if (webhook is not null && webhook.IsActive)
        {
            var wait = await limiter.AcquireDelayAsync(webhook.Id, webhook.RateLimitPerSecond, ct);
            if (wait > TimeSpan.Zero)
            {
                metrics.LimiterDelay();
                tracked.Status = EventStatus.RetryScheduled;
                tracked.NextAttemptAt = DateTimeOffset.UtcNow + wait;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        var sw = Stopwatch.StartNew();
        var result = webhook is null || !webhook.IsActive
            ? SendResult.NonRetryable("webhook missing or inactive")
            : await sender.SendAsync(webhook.Url, tracked.Payload, ct);
        sw.Stop();

        var now = DateTimeOffset.UtcNow;
        tracked.AttemptCount++;
        tracked.UpdatedAt = now;
        tracked.Attempts.Add(new DeliveryAttempt
        {
            EventId = ev.Id,
            AttemptNumber = tracked.AttemptCount,
            AttemptedAt = now,
            ResponseStatusCode = result.StatusCode,
            Error = result.Error,
            Succeeded = result.Success,
            DurationMs = (int)sw.ElapsedMilliseconds
        });

        if (result.Success)
        {
            metrics.SendOk();
            tracked.Status = EventStatus.Delivered;
            tracked.DeliveredAt = now;
        }
        else if (!result.IsRetryable)
        {
            metrics.SendNonRetryable();
            tracked.Status = EventStatus.DeadLettered;
            db.DeadLetters.Add(new DeadLetterItem
            {
                EventId = ev.Id,
                Reason = result.Error ?? "non-retryable",
                DeadLetteredAt = now
            });
        }
        else
        {
            metrics.SendRetryable();
            // Placeholder flat delay — настоящий backoff+jitter это TASK-005.
            tracked.Status = EventStatus.RetryScheduled;
            tracked.NextAttemptAt = now + retryDelay;
        }

        await db.SaveChangesAsync(ct);
        await feed.PublishAsync(ev.Id, tracked.Status.ToString(), ct);
    }
}
