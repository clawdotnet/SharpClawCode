using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Operational;

/// <summary>
/// Structured session inspection for CLI/JSON output.
/// </summary>
/// <param name="SchemaVersion">Contract version.</param>
/// <param name="WorkspaceRoot">Workspace root.</param>
/// <param name="Session">Session snapshot.</param>
/// <param name="PersistedEventCount">Events in the append-only session log.</param>
/// <param name="RecentEventSummary">Optional short text summarizing last events.</param>
/// <param name="UndoRedo">Optional checkpoint-backed undo/redo snapshot.</param>
public sealed record SessionInspectionReport(
    string SchemaVersion,
    string WorkspaceRoot,
    ConversationSession Session,
    int PersistedEventCount,
    string? RecentEventSummary,
    UndoRedoSnapshot? UndoRedo = null);
