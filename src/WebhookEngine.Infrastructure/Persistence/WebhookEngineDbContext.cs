using Microsoft.EntityFrameworkCore;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Infrastructure.Persistence;

public sealed class WebhookEngineDbContext(DbContextOptions<WebhookEngineDbContext> options)
    : DbContext(options)
{
    public DbSet<WebhookEndpoint> Webhooks => Set<WebhookEndpoint>();
    public DbSet<NotificationEvent> Events => Set<NotificationEvent>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<DeadLetterItem> DeadLetters => Set<DeadLetterItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WebhookEndpoint>(e =>
        {
            e.HasIndex(w => w.Url);
            e.Property(w => w.Url).HasMaxLength(2048);
        });

        modelBuilder.Entity<NotificationEvent>(e =>
        {
            e.HasIndex(n => n.IdempotencyKey).IsUnique();
            e.HasIndex(n => new { n.Status, n.NextAttemptAt });
            // GET /events orders by UpdatedAt DESC every dashboard poll —
            // without this index it's a seq scan + sort per request.
            e.HasIndex(n => n.UpdatedAt);
            e.Property(n => n.Payload).HasColumnType("jsonb");
            e.Property(n => n.IdempotencyKey).HasMaxLength(64);
            e.HasOne(n => n.Webhook).WithMany(w => w.Events).HasForeignKey(n => n.WebhookId);
        });

        modelBuilder.Entity<DeliveryAttempt>(e =>
        {
            e.HasIndex(a => new { a.EventId, a.AttemptNumber });
            e.HasOne(a => a.Event).WithMany(n => n.Attempts).HasForeignKey(a => a.EventId);
        });

        modelBuilder.Entity<DeadLetterItem>(e =>
        {
            e.HasIndex(d => d.DeadLetteredAt);
            e.HasOne(d => d.Event).WithMany().HasForeignKey(d => d.EventId);
        });
    }
}
