namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a plugin directory is removed from the local workspace catalog.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier (may be <c>system</c> for CLI-driven changes).</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="PluginId">The removed plugin identifier.</param>
public sealed record PluginUninstalledEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string PluginId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
