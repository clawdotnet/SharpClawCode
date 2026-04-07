using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when the runtime requests a permission decision.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="Request">The tool request that triggered the permission prompt.</param>
public sealed record PermissionRequestedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    ToolExecutionRequest Request) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
