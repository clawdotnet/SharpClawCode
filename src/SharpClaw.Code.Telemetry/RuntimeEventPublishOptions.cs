namespace SharpClaw.Code.Telemetry;

/// <summary>
/// Controls how a <see cref="SharpClaw.Code.Protocol.Events.RuntimeEvent" /> is routed after publish.
/// </summary>
/// <param name="WorkspacePath">Normalized workspace root when persisting to session NDJSON.</param>
/// <param name="SessionId">Session id when persisting; may be <c>system</c> for non-session emissions.</param>
/// <param name="PersistToSessionStore">When <see langword="true" /> and paths are set, delegates durable append to the registered persistence bridge (session event store).</param>
/// <param name="ThrowIfPersistenceFails">When <see langword="true" />, persistence exceptions propagate after logging.</param>
/// <param name="HostContext">Optional embedded host and tenant context for streaming sinks.</param>
public sealed record RuntimeEventPublishOptions(
    string? WorkspacePath = null,
    string? SessionId = null,
    bool PersistToSessionStore = false,
    bool ThrowIfPersistenceFails = false,
    SharpClaw.Code.Protocol.Models.RuntimeHostContext? HostContext = null)
{
    /// <summary>
    /// Gets a value indicating whether the session persistence bridge should be invoked.
    /// </summary>
    public bool ShouldPersist =>
        PersistToSessionStore
        && !string.IsNullOrWhiteSpace(WorkspacePath)
        && !string.IsNullOrWhiteSpace(SessionId);
}
