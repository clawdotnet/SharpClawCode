using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>Routes progress and steering through an async-local scope, preventing cross-session and cross-tenant broadcast.</summary>
public sealed class LiveTurnControlService : ILiveTurnControlService, IRuntimeEventSink
{
    private readonly AsyncLocal<LiveTurnHandle?> current = new();
    internal LiveTurnHandle? Current => current.Value;

    /// <inheritdoc />
    public LiveTurnHandle Begin(CancellationToken cancellationToken = default)
    {
        var previous = current.Value;
        var handle = new LiveTurnHandle(cancellationToken, () => current.Value = previous);
        current.Value = handle;
        return handle;
    }

    /// <inheritdoc />
    public Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
    {
        current.Value?.Publish(envelope);
        return Task.CompletedTask;
    }
}
