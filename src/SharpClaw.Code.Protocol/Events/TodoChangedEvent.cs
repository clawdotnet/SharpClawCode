using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a tracked todo item is created, updated, completed, or removed.
/// </summary>
/// <param name="EventId">Runtime event id.</param>
/// <param name="SessionId">Session id for session-scoped todos.</param>
/// <param name="TurnId">Related turn id, if any.</param>
/// <param name="OccurredAtUtc">Timestamp.</param>
/// <param name="Action">Mutation action name.</param>
/// <param name="Todo">The mutated todo item.</param>
public sealed record TodoChangedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string Action,
    TodoItem Todo) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
