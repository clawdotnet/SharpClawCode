using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a plugin manifest is written to the local workspace catalog (install).
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier (may be <c>system</c> for CLI-driven changes).</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="PluginId">The installed plugin identifier.</param>
/// <param name="Version">The declared manifest version.</param>
/// <param name="Trust">The declared trust tier (permission policy input).</param>
public sealed record PluginInstalledEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string PluginId,
    string Version,
    PluginTrustLevel Trust) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
