namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Transport-neutral editor context for IDE bridge ingress and prompt enrichment.
/// </summary>
/// <param name="WorkspaceRoot">Workspace root path from the editor.</param>
/// <param name="CurrentFilePath">Optional workspace-relative or absolute active file path.</param>
/// <param name="Selection">Optional selection.</param>
/// <param name="SessionId">Optional session SharpClaw should attach to for the next operation.</param>
public sealed record EditorContextPayload(
    string WorkspaceRoot,
    string? CurrentFilePath,
    TextSelectionRange? Selection,
    string? SessionId);
