namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Stable metadata keys for workflow features (session, turns, prompts).
/// </summary>
public static class SharpClawWorkflowMetadataKeys
{
    /// <summary>Serialized <see cref="Enums.PrimaryMode"/> for the session.</summary>
    public const string PrimaryMode = "sharpclaw.primaryMode";

    /// <summary>Parent session id after a fork.</summary>
    public const string ParentSessionId = "sharpclaw.parentSessionId";

    /// <summary>Optional checkpoint id the fork was taken from.</summary>
    public const string ForkedFromCheckpointId = "sharpclaw.forkedFromCheckpointId";

    /// <summary>UTC ISO timestamp of fork.</summary>
    public const string ForkedAtUtc = "sharpclaw.forkedAtUtc";

    /// <summary>Compact history summary copied into a forked child session.</summary>
    public const string ForkHistorySummary = "sharpclaw.forkHistorySummary";

    /// <summary>Original user prompt before @file expansion (turn metadata).</summary>
    public const string OriginalPrompt = "sharpclaw.originalPrompt";

    /// <summary>JSON array of resolved <see cref="PromptReference"/> for tracing.</summary>
    public const string PromptReferencesJson = "sharpclaw.promptReferencesJson";

    /// <summary>Custom command name when invoked from a command file.</summary>
    public const string CustomCommandName = "sharpclaw.customCommandName";

    /// <summary>JSON <see cref="UndoRedoStateDocument"/> for checkpoint-backed undo/redo.</summary>
    public const string UndoRedoStateJson = "sharpclaw.undoRedoStateJson";

    /// <summary>Optional session label for multi-session UX (non-authoritative).</summary>
    public const string SessionLabel = "sharpclaw.sessionLabel";

    /// <summary>Workspace key for attaching editor/IDE context (normalized path).</summary>
    public const string EditorContextJson = "sharpclaw.editorContextJson";

    /// <summary>Active agent id persisted for the session.</summary>
    public const string ActiveAgentId = "sharpclaw.activeAgentId";

    /// <summary>Optional compacted session summary persisted for prompt reuse.</summary>
    public const string CompactedSummary = "sharpclaw.compactedSummary";

    /// <summary>Optional share id associated with the session.</summary>
    public const string ShareId = "sharpclaw.shareId";

    /// <summary>Optional share URL associated with the session.</summary>
    public const string ShareUrl = "sharpclaw.shareUrl";

    /// <summary>UTC ISO timestamp when the session was shared.</summary>
    public const string SharedAtUtc = "sharpclaw.sharedAtUtc";

    /// <summary>Additional configured system instructions for the effective agent.</summary>
    public const string AgentInstructionAppendix = "sharpclaw.agentInstructionAppendix";

    /// <summary>JSON array of allowed tool names resolved for the effective agent.</summary>
    public const string AgentAllowedToolsJson = "sharpclaw.agentAllowedToolsJson";

    /// <summary>JSON array of session-scoped <see cref="TodoItem"/> records.</summary>
    public const string SessionTodosJson = "sharpclaw.sessionTodosJson";
}
