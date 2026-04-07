using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Persists append-only runtime events.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends a runtime event to the durable event log.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="runtimeEvent">The runtime event to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AppendAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all runtime events for a session.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session runtime events.</returns>
    Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);
}
