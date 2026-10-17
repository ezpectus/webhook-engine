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
        app.MapPost("/events", IngestEvent);
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

    private static async Task<IResult> IngestEvent(
        IngestRequest req,
        WebhookEngineDbContext db,
        IDeduplicationStore dedup,
        IConfiguration config,
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
                return Duplicate(seen);
            // Redis помнит ключ, а в PG строки нет (кеш протёрли / insert не
            // доехал) — пропускаем в insert: индекс разрулит истину.
        }

        var ev = new NotificationEvent
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            IdempotencyKey = key,
            Payload = req.Payload,
            Status = EventStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Events.Add(ev);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            var seen = await db.Events.AsNoTracking()
                .FirstAsync(e => e.IdempotencyKey == key, ct);
            return Duplicate(seen);
        }

        var window = TimeSpan.FromHours(config.GetValue("Dedup:WindowHours", 24));
        await dedup.MarkAsync(key, window, ct);

        return Results.Accepted(value: new { eventId = ev.Id, status = "accepted" });
    }

    private static string ComputeKey(Guid webhookId, string payload)
        => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{webhookId}:{payload}"))).ToLowerInvariant();

    private static IResult Duplicate(NotificationEvent e)
        => Results.Ok(new { eventId = e.Id, status = "duplicate", eventStatus = e.Status.ToString() });

    private sealed record IngestRequest(Guid WebhookId, string Payload);
    private sealed record RegisterWebhookRequest(string Url, string? Secret, int? RateLimitPerSecond);
}
