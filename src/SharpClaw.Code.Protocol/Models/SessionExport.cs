namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Supported session export formats.
/// </summary>
public enum SessionExportFormat
{
    /// <summary>Markdown document.</summary>
    Markdown,

    /// <summary>JSON document with schema version.</summary>
    Json,
}

/// <summary>
/// A single tool-related line item for export.
/// </summary>
public sealed record SessionExportToolAction(
    string TurnId,
    string ToolName,
    bool Succeeded,
    string? Summary);

/// <summary>
/// One turn row in an export.
/// </summary>
public sealed record SessionExportTurn(
    string TurnId,
    int SequenceNumber,
    string? Input,
    string? Output,
    List<SessionExportToolAction> NotableToolActions);

/// <summary>
/// Full export payload for JSON (and drives Markdown rendering).
/// </summary>
/// <param name="SchemaVersion">Export contract version.</param>
/// <param name="ExportedAtUtc">When the export was produced.</param>
/// <param name="Session">Session snapshot at export time.</param>
/// <param name="Turns">Ordered turns.</param>
/// <param name="SelectedEventSummary">Optional short notes from events.</param>
public sealed record SessionExportDocument(
    string SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    ConversationSession Session,
    List<SessionExportTurn> Turns,
    List<string>? SelectedEventSummary);
