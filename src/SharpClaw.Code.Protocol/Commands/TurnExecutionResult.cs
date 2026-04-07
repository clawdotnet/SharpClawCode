using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents the full result of executing a single turn.
/// </summary>
/// <param name="Session">The resulting session snapshot.</param>
/// <param name="Turn">The resulting turn snapshot.</param>
/// <param name="FinalOutput">The final output rendered for the turn, if any.</param>
/// <param name="ToolResults">The tool results captured during execution.</param>
/// <param name="Usage">The aggregate usage snapshot for the turn.</param>
/// <param name="Checkpoint">The checkpoint captured for recovery, if any.</param>
/// <param name="Events">The runtime events emitted during execution.</param>
public sealed record TurnExecutionResult(
    ConversationSession Session,
    ConversationTurn Turn,
    string? FinalOutput,
    ToolResult[] ToolResults,
    UsageSnapshot? Usage,
    RuntimeCheckpoint? Checkpoint,
    RuntimeEvent[] Events);
