using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Durable append of <see cref="RuntimeEvent" /> records. Implemented in the Sessions layer over the append-only event store.
/// Telemetry does not own file layout; this adapter bridges publish to NDJSON storage.
/// </summary>
public interface IRuntimeEventPersistence
{
    /// <summary>
    /// Persists one event for replay under the session store for the workspace.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="runtimeEvent">The event to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task PersistAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Persists several events in order, in one store operation where the backend supports it.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="runtimeEvents">The events to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    async Task PersistRangeAsync(string workspacePath, string sessionId, IReadOnlyList<RuntimeEvent> runtimeEvents, CancellationToken cancellationToken)
    {
        foreach (var runtimeEvent in runtimeEvents)
        {
            await PersistAsync(workspacePath, sessionId, runtimeEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
