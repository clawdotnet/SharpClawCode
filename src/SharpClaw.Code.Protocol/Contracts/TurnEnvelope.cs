namespace SharpClaw.Code.Protocol.Contracts;

/// <summary>
/// Represents the minimal protocol contract for a single agent turn.
/// </summary>
/// <param name="SessionId">The logical session identifier.</param>
/// <param name="Input">The turn input text.</param>
public sealed record TurnEnvelope(string SessionId, string Input);
