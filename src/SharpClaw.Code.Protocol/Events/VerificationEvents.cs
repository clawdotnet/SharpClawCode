using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Protocol.Events;
/// <summary>Raised before an authorized verification attempt; zero is the initial check.</summary>
public sealed record VerificationStartedEvent(string EventId, string SessionId, string? TurnId, DateTimeOffset OccurredAtUtc, int Attempt, VerificationScope Scope, int MaxRepairIterations) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
/// <summary>Raised once with a completed verification report.</summary>
public sealed record VerificationCompletedEvent(string EventId, string SessionId, string? TurnId, DateTimeOffset OccurredAtUtc, int Attempt, VerificationRunReport Report) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
/// <summary>Durably consumes a repair attempt before invoking the agent.</summary>
public sealed record VerificationRepairStartedEvent(string EventId, string SessionId, string? TurnId, DateTimeOffset OccurredAtUtc, int Attempt) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
/// <summary>Reports the repair agent's completion without implying that re-verification passed.</summary>
public sealed record VerificationRepairCompletedEvent(string EventId, string SessionId, string? TurnId, DateTimeOffset OccurredAtUtc, int Attempt, bool Succeeded, string Summary) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
