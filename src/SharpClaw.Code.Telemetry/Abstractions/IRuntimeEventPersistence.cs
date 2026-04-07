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
}
