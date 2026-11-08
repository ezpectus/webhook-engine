using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Delivery;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Api.Workers;

public sealed class DeliveryWorker(
    IEventQueue queue,
    IServiceProvider services,
    IConfiguration config,
    IRetryScheduler scheduler,
    Core.RetryScheduling.BackoffPolicy backoff,
    TimeProvider time,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parallelism = config.GetValue("Delivery:WorkerParallelism", 4);

        // N parallel channel readers: each item goes to exactly one worker.
        // Bottleneck is outbound HTTP, not CPU — 4 by default.
        var loops = Enumerable.Range(0, parallelism)
            .Select(_ => RunLoopAsync(stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
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
                await ProcessAsync(ev, db, sender, feed, limiter, metrics, backoff, scheduler, time, stoppingToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // H-3: the lease died mid-flight (reclaim bumped the version).
                // The whole write rolls back — no half-written result, no
                // feed publish of a status that never persisted. Expected
                // mechanism under lease loss, not an error.
                logger.LogInformation("Dropped stale write for {EventId} — lease lost", ev.Id);
            }
            catch (Exception ex)
            {
                // Event stays InFlight — the sweeper's stale lease returns it to Pending.
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
        Core.RetryScheduling.BackoffPolicy backoff,
        IRetryScheduler scheduler,
        TimeProvider time,
        CancellationToken ct)
    {
        var tracked = await db.Events.FindAsync([ev.Id], ct);
        // The channel may hand out a stale item: while it waited, the event
        // could have been delivered, replayed or dead-lettered. Only
        // InFlight enters the queue — everything else is skipped (B-review).
        if (tracked is null || tracked.Status != EventStatus.InFlight)
            return;

        var webhook = await db.Webhooks.FindAsync([ev.WebhookId], ct);

        // D7: rate-limit delay uses the same deferred-send mechanism as
        // retries. This is NOT an attempt: AttemptCount untouched, no
        // attempt row written.
        if (webhook is not null && webhook.IsActive)
        {
            var wait = await limiter.AcquireDelayAsync(webhook.Id, webhook.RateLimitPerSecond, ct);
            if (wait > TimeSpan.Zero)
            {
                metrics.LimiterDelay();
                tracked.Status = EventStatus.RetryScheduled;
                var deferUntil = time.GetUtcNow();
                tracked.NextAttemptAt = deferUntil + wait;
                tracked.UpdatedAt = deferUntil;
                await db.SaveChangesAsync(ct);
                await scheduler.ScheduleAsync(ev.Id, tracked.NextAttemptAt.Value, ct);
                return;
            }
        }

        var sw = Stopwatch.StartNew();
        var result = webhook is null || !webhook.IsActive
            ? SendResult.NonRetryable("webhook missing or inactive")
            : await sender.SendAsync(webhook.Url, tracked.Payload, ct, webhook.Secret);
        sw.Stop();

        var now = time.GetUtcNow();
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
            if (backoff.IsExhausted(tracked.AttemptCount))
            {
                // Retries exhausted — terminal state, manual triage.
                tracked.Status = EventStatus.DeadLettered;
                db.DeadLetters.Add(new DeadLetterItem
                {
                    EventId = ev.Id,
                    Reason = $"max attempts ({backoff.MaxAttempts}) exhausted: {result.Error}",
                    DeadLetteredAt = now
                });
            }
            else
            {
                tracked.Status = EventStatus.RetryScheduled;
                // attemptCount-1: ComputeDelay expects a 0-based attempt
                // number, and the counter is already incremented (1st
                // failure → 1). Was ComputeDelay(AttemptCount) → first
                // delay was 2·base instead of base.
                tracked.NextAttemptAt = now + backoff.ComputeDelay(tracked.AttemptCount - 1);
            }
        }

        await db.SaveChangesAsync(ct);

        // The heap is an accelerator over PG: durable write first, then
        // schedule. Order is safe: a heap entry without PG — the claim
        // fails and the entry quietly expires; PG without heap — the
        // sweeper scan restores it.
        if (tracked.Status == EventStatus.RetryScheduled && tracked.NextAttemptAt is { } next)
            await scheduler.ScheduleAsync(ev.Id, next, ct);

        await feed.PublishAsync(ev.Id, tracked.Status.ToString(), ct);
    }
}
