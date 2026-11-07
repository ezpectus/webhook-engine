using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Persistence;

namespace WebhookEngine.Api.Endpoints;

public static class Endpoints
{
    public static void MapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks", RegisterWebhook);
        app.MapGet("/webhooks", ListWebhooks);
        app.MapPost("/events", IngestEvent);
        app.MapGet("/deadletters", ListDeadLetters);
        app.MapPost("/deadletters/{eventId:guid}/replay", ReplayDeadLetter);
        app.MapGet("/stats", GetStats);
        app.MapGet("/metrics", GetMetrics);
        app.MapGet("/events/{id:guid}", GetEvent);
        app.MapGet("/events", ListEvents);
    }

    private static async Task<IResult> RegisterWebhook(
        RegisterWebhookRequest req, WebhookEngineDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Url) || !Uri.TryCreate(req.Url, UriKind.Absolute, out _))
            return Results.BadRequest(new { error = "valid url required" });
        // Review fix: rate <= 0 is valid at intake but breaks deeper —
        // TokenBucket would throw ArgumentOutOfRangeException on every
        // send → the event would bounce InFlight↔Pending forever (crash
        // loop, dup spam). Reject at the boundary, same as a bad URL.
        // Was: RateLimitPerSecond = req.RateLimitPerSecond ?? 10 — unchecked.
        if (req.RateLimitPerSecond is <= 0)
            return Results.BadRequest(new { error = "rateLimitPerSecond must be > 0" });

        var hook = new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            Url = req.Url,
            Secret = req.Secret,
            RateLimitPerSecond = req.RateLimitPerSecond ?? 10,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Webhooks.Add(hook);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/webhooks/{hook.Id}", new { webhookId = hook.Id });
    }

    private static async Task<IResult> ListWebhooks(
        WebhookEngineDbContext db, CancellationToken ct)
    {
        var items = await db.Webhooks.AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .Select(w => new { w.Id, w.Url, w.RateLimitPerSecond, w.IsActive, w.CreatedAt })
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> IngestEvent(
        IngestRequest req,
        WebhookEngineDbContext db,
        IDeduplicationStore dedup,
        IEventQueue queue,
        IConfiguration config,
        WebhookEngine.Core.Metrics.EngineMetrics metrics,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Payload))
            return Results.BadRequest(new { error = "payload required" });

        var webhook = await db.Webhooks.FindAsync([req.WebhookId], ct);
        if (webhook is null)
            return Results.BadRequest(new { error = "unknown webhookId" });
        if (!webhook.IsActive)
            return Results.BadRequest(new { error = "webhook inactive" });

        var windowHours = config.GetValue("Dedup:WindowHours", 24.0);
        if (windowHours <= 0) windowHours = 1;
        var windowSecs = Math.Max(1L, (long)(windowHours * 3600));
        var bucket = time.GetUtcNow().ToUnixTimeSeconds() / windowSecs;
        var redisKey = ComputeKey(req.WebhookId, req.Payload);
        var key = ComputeKey(req.WebhookId, req.Payload, bucket);

        var seenId = await dedup.GetEventIdAsync(redisKey, ct);
        if (seenId != Guid.Empty)
        {
            metrics.DedupHit();
            return Results.Ok(new { eventId = seenId, status = "duplicate" });
        }

        // Backpressure: reject only when the persistent queue is bloated (variant D with a ceiling).
        var threshold = config.GetValue("Queue:PendingThreshold", 100_000);
        var depth = await db.Events.CountAsync(
            e => e.Status != EventStatus.Delivered && e.Status != EventStatus.DeadLettered, ct);
        if (depth >= threshold)
        {
            metrics.Rejected();
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var now = time.GetUtcNow();
        var ev = new NotificationEvent
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            IdempotencyKey = key,
            Payload = req.Payload,
            Status = EventStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Events.Add(ev);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            metrics.DedupHit();
            var seen = await db.Events.AsNoTracking()
                .FirstAsync(e => e.IdempotencyKey == key, ct);
            return Duplicate(seen);
        }

        // Fast path. Atomic claim Pending→InFlight: whoever's UPDATE
        // lands does the enqueue. A sweeper that raced in first can't
        // cause a double enqueue (race B3).
        var claimed = await db.Events
            .Where(e => e.Id == ev.Id && e.Status == EventStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.InFlight)
                .SetProperty(e => e.UpdatedAt, now), ct);

        if (claimed == 1 && !queue.TryEnqueue(ev))
        {
            // Channel full — roll back to Pending, the sweeper will pick it up.
            await db.Events
                .Where(e => e.Id == ev.Id && e.Status == EventStatus.InFlight)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, EventStatus.Pending)
                    .SetProperty(e => e.UpdatedAt, now), ct);
            ev.Status = EventStatus.Pending;
        }
        else if (claimed == 1)
        {
            ev.Status = EventStatus.InFlight;
        }

        var window = TimeSpan.FromHours(windowHours);
        await dedup.MarkAsync(redisKey, ev.Id, window, ct);

        metrics.Accepted();
        return Results.Accepted(value: new { eventId = ev.Id, status = "accepted" });
    }

    private static async Task<IResult> ListDeadLetters(
        WebhookEngineDbContext db, CancellationToken ct)
    {
        var items = await db.DeadLetters.AsNoTracking()
            .OrderByDescending(d => d.DeadLetteredAt)
            .Take(200)
            .Select(d => new { d.EventId, d.Reason, d.DeadLetteredAt, d.ReplayedAt })
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> ReplayDeadLetter(
        Guid eventId, WebhookEngineDbContext db, IEventQueue queue, CancellationToken ct)
    {
        var dl = await db.DeadLetters
            .FirstOrDefaultAsync(d => d.EventId == eventId && d.ReplayedAt == null, ct);
        if (dl is null)
            return Results.NotFound(new { error = "no live dead letter for event" });

        var ev = await db.Events.FindAsync([eventId], ct);
        if (ev is null || ev.Status != EventStatus.DeadLettered)
            return Results.Conflict(new { error = "event not in DeadLettered state" });

        // D12: replay bypasses dedup (the same key would be eaten by the filter).
        // Attempts history is an audit trail; AttemptCount resets — a fresh
        // pass gets a fresh retry budget.
        var now = DateTimeOffset.UtcNow;
        ev.AttemptCount = 0;
        ev.NextAttemptAt = null;
        ev.UpdatedAt = now;
        dl.ReplayedAt = now;

        // Review fix: durable write first, enqueue second — same as ingest.
        // Was: TryEnqueue before SaveChanges → a worker could pick up the
        // item while PG still said DeadLettered, skip it as stale, and the
        // row would sit InFlight until lease expiry (5 min delay). Now:
        // claim InFlight → enqueue → didn't fit → roll back to Pending.
        ev.Status = EventStatus.InFlight;
        await db.SaveChangesAsync(ct);
        if (!queue.TryEnqueue(ev))
        {
            await db.Events
                .Where(e => e.Id == ev.Id && e.Status == EventStatus.InFlight)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, EventStatus.Pending)
                    .SetProperty(e => e.UpdatedAt, now), ct);
            ev.Status = EventStatus.Pending;
        }

        return Results.Accepted(value: new { eventId = ev.Id, status = ev.Status.ToString() });
    }

    private static async Task<IResult> GetStats(
        WebhookEngineDbContext db, StatsCache stats, CancellationToken ct)
    {
        var s = await stats.GetAsync(db, ct);
        return Results.Ok(new { byStatus = s.ByStatus, liveDeadLetters = s.LiveDeadLetters });
    }

    private static async Task<IResult> GetEvent(
        Guid id, WebhookEngineDbContext db, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new
            {
                e.Id, e.WebhookId, e.Status, e.AttemptCount,
                e.NextAttemptAt, e.DeliveredAt, e.CreatedAt, e.UpdatedAt,
                Attempts = e.Attempts
                    .OrderBy(a => a.AttemptNumber)
                    .Select(a => new
                    {
                        a.AttemptNumber, a.AttemptedAt, a.ResponseStatusCode,
                        a.Error, a.Succeeded, a.DurationMs
                    })
            })
            .FirstOrDefaultAsync(ct);
        return ev is null ? Results.NotFound() : Results.Ok(ev);
    }

    private static async Task<IResult> ListEvents(
        WebhookEngineDbContext db, CancellationToken ct, int take = 50, string? status = null)
    {
        take = Math.Clamp(take, 1, 500);
        var q = db.Events.AsNoTracking();
        if (Enum.TryParse<EventStatus>(status, true, out var s))
            q = q.Where(e => e.Status == s);
        var items = await q.OrderByDescending(e => e.UpdatedAt).Take(take)
            .Select(e => new
            {
                e.Id, e.WebhookId, e.Status, e.AttemptCount,
                e.NextAttemptAt, e.UpdatedAt
            })
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetMetrics(
        WebhookEngineDbContext db,
        WebhookEngine.Core.Metrics.EngineMetrics m,
        StatsCache stats,
        CancellationToken ct)
    {
        var s = await stats.GetAsync(db, ct);
        var sent = m.SendsOk + m.SendsRetryable + m.SendsNonRetryable;

        return Results.Ok(new
        {
            dedup_hit_ratio = m.DedupHits + m.EventsAccepted > 0
                ? (double)m.DedupHits / (m.DedupHits + m.EventsAccepted) : 0.0,
            delivery_retry_rate = sent > 0 ? (double)m.SendsRetryable / sent : 0.0,
            queue_depth = s.Depth,
            dlq_live = s.LiveDeadLetters,
            counters = new
            {
                m.EventsAccepted, m.EventsRejected, m.DedupHits,
                m.SendsOk, m.SendsRetryable, m.SendsNonRetryable,
                m.LimiterDelays, m.StaleReclaims
            },
            byStatus = s.ByStatus,
            delivered = s.Delivered
        });
    }

    private static string ComputeKey(Guid webhookId, string payload, long? bucket = null)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                bucket is null ? $"{webhookId}:{payload}" : $"{webhookId}:{payload}:{bucket}"))).ToLowerInvariant();

    private static IResult Duplicate(NotificationEvent e)
        => Results.Ok(new { eventId = e.Id, status = "duplicate", eventStatus = e.Status.ToString() });

    private sealed record IngestRequest(Guid WebhookId, string Payload);
    private sealed record RegisterWebhookRequest(string Url, string? Secret, int? RateLimitPerSecond);
}
