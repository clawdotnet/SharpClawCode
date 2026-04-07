using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when tool execution begins.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="Request">The tool request being executed.</param>
public sealed record ToolStartedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    ToolExecutionRequest Request) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
