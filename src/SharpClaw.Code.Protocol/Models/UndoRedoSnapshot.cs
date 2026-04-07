namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Summarizes undo/redo availability for inspection and status surfaces.
/// </summary>
public sealed record UndoRedoSnapshot(
    int TotalMutationSets,
    int AppliedMutationSets,
    int RedoAvailable,
    string? LastAppliedMutationSetId,
    bool UndoRedoMetadataCorrupted = false);
