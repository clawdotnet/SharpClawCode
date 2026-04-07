namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// A single reversible workspace file change performed by a SharpClaw tool execution.
/// </summary>
/// <param name="OperationId">Stable id for correlation in events.</param>
/// <param name="Kind">Mutation kind for inverse/forward semantics.</param>
/// <param name="ToolName">Tool that performed the mutation.</param>
/// <param name="RelativePath">Path relative to workspace root, normalized separators.</param>
/// <param name="ContentBefore">Content before the mutation (null when the file did not exist).</param>
/// <param name="ContentAfter">Content after the mutation (empty string allowed).</param>
public sealed record FileMutationOperation(
    string OperationId,
    FileMutationKind Kind,
    string ToolName,
    string RelativePath,
    string? ContentBefore,
    string ContentAfter);
