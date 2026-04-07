using System.Text.Json;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Runtime.Mutations;

/// <summary>
/// Result of parsing persisted undo/redo metadata from session workflow keys.
/// </summary>
/// <param name="State">The deserialized document or an empty document when missing/invalid.</param>
/// <param name="IsCorrupt">True when JSON was present but failed to deserialize.</param>
/// <param name="Detail">Human-readable detail when <see cref="IsCorrupt"/> is true.</param>
internal readonly record struct UndoRedoParseResult(UndoRedoStateDocument State, bool IsCorrupt, string? Detail);

internal static class UndoRedoStateHelper
{
    /// <summary>
    /// Parses undo/redo state from metadata without swallowing corruption.
    /// </summary>
    public static UndoRedoParseResult Parse(Dictionary<string, string>? metadata)
    {
        var md = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
        if (!md.TryGetValue(SharpClawWorkflowMetadataKeys.UndoRedoStateJson, out var json)
            || string.IsNullOrWhiteSpace(json))
        {
            return new UndoRedoParseResult(new UndoRedoStateDocument([], 0, []), false, null);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.UndoRedoStateDocument);
            if (parsed is null)
            {
                return new UndoRedoParseResult(
                    new UndoRedoStateDocument([], 0, []),
                    true,
                    "Undo/redo metadata deserialized to null.");
            }

            return new UndoRedoParseResult(
                new UndoRedoStateDocument(
                    parsed.MutationHistory ?? [],
                    parsed.AppliedPrefixLength,
                    parsed.RedoStack ?? []),
                false,
                null);
        }
        catch (JsonException ex)
        {
            return new UndoRedoParseResult(new UndoRedoStateDocument([], 0, []), true, ex.Message);
        }
    }

    public static void Assign(Dictionary<string, string> metadata, UndoRedoStateDocument state)
    {
        metadata[SharpClawWorkflowMetadataKeys.UndoRedoStateJson] =
            JsonSerializer.Serialize(state, ProtocolJsonContext.Default.UndoRedoStateDocument);
    }
}
