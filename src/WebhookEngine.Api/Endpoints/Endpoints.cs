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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Payload))
            return Results.BadRequest(new { error = "payload required" });

        var webhook = await db.Webhooks.FindAsync([req.WebhookId], ct);
        if (webhook is null)
            return Results.BadRequest(new { error = "unknown webhookId" });
        if (!webhook.IsActive)
            return Results.BadRequest(new { error = "webhook inactive" });

        var key = ComputeKey(req.WebhookId, req.Payload);

        if (await dedup.IsKnownAsync(key, ct))
        {
            var seen = await db.Events.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);
            if (seen is not null)
            {
                metrics.DedupHit();
                return Duplicate(seen);
            }
            // Redis помнит ключ, а в PG строки нет (кеш протёрли / insert не
            // доехал) — пропускаем в insert: индекс разрулит истину.
        }

        // Backpressure: отказ только при раздутой персистентной очереди (вариант D с потолком).
        var threshold = config.GetValue("Queue:PendingThreshold", 100_000);
        var depth = await db.Events.CountAsync(
            e => e.Status != EventStatus.Delivered && e.Status != EventStatus.DeadLettered, ct);
        if (depth >= threshold)
        {
            metrics.Rejected();
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var now = DateTimeOffset.UtcNow;
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

        // Быстрый путь. Атомарный claim Pending→InFlight: у кого сработал
        // UPDATE — тот и enqueue'ит. Sweeper, успевший перехватить событие,
        // не может создать двойной enqueue (гонка В3).
        var claimed = await db.Events
            .Where(e => e.Id == ev.Id && e.Status == EventStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.InFlight)
                .SetProperty(e => e.UpdatedAt, now), ct);

        if (claimed == 1 && !queue.TryEnqueue(ev))
        {
            // Канал полон — возвращаем в Pending, доберёт sweeper.
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

        var window = TimeSpan.FromHours(config.GetValue("Dedup:WindowHours", 24));
        await dedup.MarkAsync(key, window, ct);

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

        // D12: replay идёт в обход dedup (тот же ключ был бы съеден фильтром).
        // История Attempts — аудит, AttemptCount обнуляем: новый заход получает
        // свежий бюджет ретраев.
        var now = DateTimeOffset.UtcNow;
        ev.Status = queue.TryEnqueue(ev) ? EventStatus.InFlight : EventStatus.Pending;
        ev.AttemptCount = 0;
        ev.NextAttemptAt = null;
        ev.UpdatedAt = now;
        dl.ReplayedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Accepted(value: new { eventId = ev.Id, status = ev.Status.ToString() });
    }

    private static async Task<IResult> GetStats(
        WebhookEngineDbContext db, CancellationToken ct)
    {
        var byStatus = await db.Events.AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var liveDeadLetters = await db.DeadLetters
            .CountAsync(d => d.ReplayedAt == null, ct);
        return Results.Ok(new
        {
            byStatus = byStatus.ToDictionary(x => x.Status.ToString(), x => x.Count),
            liveDeadLetters
        });
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
        CancellationToken ct)
    {
        var depth = await db.Events.CountAsync(
            e => e.Status != EventStatus.Delivered && e.Status != EventStatus.DeadLettered, ct);
        var dlqLive = await db.DeadLetters.CountAsync(d => d.ReplayedAt == null, ct);
        var byStatus = await db.Events.AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key.ToString(), x => x.Count, ct);
        var delivered = await db.Events.CountAsync(e => e.Status == EventStatus.Delivered, ct);
        var sent = m.SendsOk + m.SendsRetryable + m.SendsNonRetryable;

        return Results.Ok(new
        {
            dedup_hit_ratio = m.DedupHits + m.EventsAccepted > 0
                ? (double)m.DedupHits / (m.DedupHits + m.EventsAccepted) : 0.0,
            delivery_retry_rate = sent > 0 ? (double)m.SendsRetryable / sent : 0.0,
            queue_depth = depth,
            dlq_live = dlqLive,
            counters = new
            {
                m.EventsAccepted, m.EventsRejected, m.DedupHits,
                m.SendsOk, m.SendsRetryable, m.SendsNonRetryable,
                m.LimiterDelays, m.StaleReclaims
            },
            byStatus,
            delivered
        });
    }

    private static string ComputeKey(Guid webhookId, string payload)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{webhookId}:{payload}"))).ToLowerInvariant();

    private static IResult Duplicate(NotificationEvent e)
        => Results.Ok(new { eventId = e.Id, status = "duplicate", eventStatus = e.Status.ToString() });

    private sealed record IngestRequest(Guid WebhookId, string Payload);
    private sealed record RegisterWebhookRequest(string Url, string? Secret, int? RateLimitPerSecond);
}
