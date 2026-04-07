namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Serializable undo/redo cursor state stored in <see cref="ConversationSession.Metadata"/>.
/// </summary>
/// <param name="MutationHistory">Ordered mutation set ids (aligned with successful turns that recorded mutations).</param>
/// <param name="AppliedPrefixLength">How many entries from the start of <see cref="MutationHistory"/> are currently applied on disk.</param>
/// <param name="RedoStack">LIFO stack of mutation set ids available for redo (most recent undo at the end).</param>
public sealed record UndoRedoStateDocument(
    List<string> MutationHistory,
    int AppliedPrefixLength,
    List<string> RedoStack)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UndoRedoStateDocument"/> class for deserialization.
    /// </summary>
    public UndoRedoStateDocument()
        : this([], 0, [])
    {
    }
}
