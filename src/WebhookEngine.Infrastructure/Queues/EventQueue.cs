using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using WebhookEngine.Core.Abstractions;
using WebhookEngine.Core.Models;

namespace WebhookEngine.Infrastructure.Queues;

public sealed class EventQueue : IEventQueue
{
    private readonly Channel<NotificationEvent> _channel;

    public EventQueue(IConfiguration config)
    {
        var capacity = config.GetValue("Queue:Capacity", 10_000);
        _channel = Channel.CreateBounded<NotificationEvent>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TryEnqueue(NotificationEvent ev) => _channel.Writer.TryWrite(ev);

    public IAsyncEnumerable<NotificationEvent> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
