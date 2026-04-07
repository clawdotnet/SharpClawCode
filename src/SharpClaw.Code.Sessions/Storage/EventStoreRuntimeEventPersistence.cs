using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Bridges <see cref="IRuntimeEventPersistence" /> to the append-only session <see cref="IEventStore" /> (NDJSON).
/// </summary>
public sealed class EventStoreRuntimeEventPersistence(IEventStore eventStore) : IRuntimeEventPersistence
{
    /// <inheritdoc />
    public Task PersistAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        => eventStore.AppendAsync(workspacePath, sessionId, runtimeEvent, cancellationToken);
}
