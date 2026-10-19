using Microsoft.Extensions.Configuration;
using WebhookEngine.Core.Models;
using WebhookEngine.Infrastructure.Queues;

namespace WebhookEngine.Tests;

public class EventQueueTests
{
    private static EventQueue MakeQueue(int capacity)
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Queue:Capacity"] = capacity.ToString()
            })
            .Build());

    private static NotificationEvent MakeEvent() => new()
    {
        Id = Guid.NewGuid(),
        WebhookId = Guid.NewGuid(),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        Payload = "{}"
    };

    [Fact]
    public void TryEnqueue_ReturnsFalse_WhenChannelFull()
    {
        var q = MakeQueue(2);
        Assert.True(q.TryEnqueue(MakeEvent()));
        Assert.True(q.TryEnqueue(MakeEvent()));
        Assert.False(q.TryEnqueue(MakeEvent()));
    }

    [Fact]
    public async Task ReadAll_ReadsInFifoOrder()
    {
        var q = MakeQueue(10);
        var e1 = MakeEvent();
        var e2 = MakeEvent();
        q.TryEnqueue(e1);
        q.TryEnqueue(e2);

        var seen = new List<Guid>();
        await foreach (var ev in q.ReadAllAsync(CancellationToken.None))
        {
            seen.Add(ev.Id);
            if (seen.Count == 2) break;
        }

        Assert.Equal(new[] { e1.Id, e2.Id }, seen);
    }
}
