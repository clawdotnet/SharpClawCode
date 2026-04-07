using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a plugin changes lifecycle state.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="PluginId">The plugin identifier.</param>
/// <param name="PreviousState">The prior lifecycle state.</param>
/// <param name="CurrentState">The new lifecycle state.</param>
/// <param name="Message">A concise transition message, if any.</param>
public sealed record PluginStateChangedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string PluginId,
    PluginLifecycleState PreviousState,
    PluginLifecycleState CurrentState,
    string? Message) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
