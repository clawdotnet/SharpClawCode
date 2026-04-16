using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// In-process fan-out stream for embedded hosts and admin APIs.
/// </summary>
public sealed class InProcessRuntimeEventStream(IOptions<TelemetryOptions> telemetryOptionsAccessor) : IRuntimeEventSink, IRuntimeEventStream
{
    private readonly ConcurrentQueue<RuntimeEventEnvelope> recent = new();
    private readonly ConcurrentDictionary<Guid, Channel<RuntimeEventEnvelope>> subscribers = new();
    private readonly int capacity = Math.Max(64, telemetryOptionsAccessor.Value.RuntimeEventRingBufferCapacity);

    /// <inheritdoc />
    public Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        recent.Enqueue(envelope);
        while (recent.Count > capacity && recent.TryDequeue(out _))
        {
        }

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(envelope);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<RuntimeEventEnvelope> GetRecentEnvelopesSnapshot()
        => recent.ToArray();

    /// <inheritdoc />
    public async IAsyncEnumerable<RuntimeEventEnvelope> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RuntimeEventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        subscribers[id] = channel;

        try
        {
            foreach (var envelope in recent)
            {
                yield return envelope;
            }

            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var envelope))
                {
                    yield return envelope;
                }
            }
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
