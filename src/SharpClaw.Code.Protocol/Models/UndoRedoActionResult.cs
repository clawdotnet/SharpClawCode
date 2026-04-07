namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Outcome of an undo or redo operation for machine-readable CLI/JSON consumers.
/// </summary>
/// <param name="Succeeded">Whether the action applied cleanly.</param>
/// <param name="Action">Either <c>undo</c> or <c>redo</c>.</param>
/// <param name="MutationSetId">Mutation set affected, when relevant.</param>
/// <param name="Message">Human-readable summary or error.</param>
/// <param name="AppliedPrefixLength">Updated applied prefix length after success.</param>
/// <param name="RedoDepthAvailable">Redo stack depth after success.</param>
public sealed record UndoRedoActionResult(
    bool Succeeded,
    string Action,
    string? MutationSetId,
    string Message,
    int AppliedPrefixLength,
    int RedoDepthAvailable);
