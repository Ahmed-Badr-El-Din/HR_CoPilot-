using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HR.Application.Abstractions;

namespace HR.Infrastructure.Observability;

/// <summary>
/// In-process publisher/subscriber used to fan run events out to connected SSE
/// clients (FR-6). Channels are kept small; if a consumer is slow it simply drops
/// the subscription rather than blocking the workflow.
/// </summary>
public sealed class InMemoryEventEmitter : IEventEmitter
{
    private readonly ConcurrentDictionary<string, Channel<EventPacket>> _channels = new();

    public void Publish(string channel, EventPacket packet)
    {
        if (!_channels.TryGetValue(channel, out var ch)) return;
        ch.Writer.TryWrite(packet);
    }

    public async IAsyncEnumerable<EventPacket> SubscribeAsync(string channel, [EnumeratorCancellation] CancellationToken ct)
    {
        var ch = _channels.GetOrAdd(channel, _ => Channel.CreateBounded<EventPacket>(new BoundedChannelOptions(512)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        }));

        await foreach (var packet in ch.Reader.ReadAllAsync(ct))
            yield return packet;
    }
}
