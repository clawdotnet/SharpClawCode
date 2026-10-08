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

    /// <summary>JSON array of approval scopes that may be auto-approved for the session.</summary>
    public const string ApprovalAutoApproveScopesJson = "sharpclaw.approvalAutoApproveScopesJson";

    /// <summary>Optional numeric auto-approval budget for the session.</summary>
    public const string ApprovalAutoApproveBudget = "sharpclaw.approvalAutoApproveBudget";

    /// <summary>Most recent deep-planning summary captured for the session.</summary>
    public const string DeepPlanningSummary = "sharpclaw.deepPlanningSummary";

    /// <summary>Most recent deep-planning next action captured for the session.</summary>
    public const string DeepPlanningNextAction = "sharpclaw.deepPlanningNextAction";

    /// <summary>Preferred permission mode persisted for the session.</summary>
    public const string PreferredPermissionMode = "sharpclaw.preferredPermissionMode";

    /// <summary>JSON array of trusted plugin ids for the session.</summary>
    public const string TrustedPluginNamesJson = "sharpclaw.trustedPluginNamesJson";

    /// <summary>JSON array of trusted MCP server names for the session.</summary>
    public const string TrustedMcpServerNamesJson = "sharpclaw.trustedMcpServerNamesJson";

    /// <summary>Serialized <see cref="SessionModelPreference"/> persisted for the session.</summary>
    public const string SessionModelPreferenceJson = "sharpclaw.sessionModelPreferenceJson";

    /// <summary>Serialized imported work item persisted for the session.</summary>
    public const string WorkItemJson = "sharpclaw.workItemJson";

    /// <summary>Short work item goal pinned to the session.</summary>
    public const string WorkItemGoal = "sharpclaw.workItemGoal";

    /// <summary>Most recently invoked skill pack id.</summary>
    public const string ActiveSkillPackId = "sharpclaw.activeSkillPackId";

    /// <summary>Most recently invoked external agent id.</summary>
    public const string LastExternalAgentId = "sharpclaw.lastExternalAgentId";

    /// <summary>Prefix for managed todo id maps keyed by owner agent id.</summary>
    public const string ManagedSessionTodoMapPrefix = "sharpclaw.managedSessionTodoMap.";

    /// <summary>
    /// Builds the session-metadata key used to map managed external task ids to persisted todo ids.
    /// </summary>
    public static string GetManagedSessionTodoMapKey(string ownerAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAgentId);
        return ManagedSessionTodoMapPrefix + ownerAgentId.Trim();
    }
    /// <summary>Overrides automatic verification enablement.</summary>
    public const string VerificationEnabled = "sharpclaw.verification.enabled";
    /// <summary>Overrides automatic verification scope.</summary>
    public const string VerificationScope = "sharpclaw.verification.scope";
    /// <summary>Overrides the bounded repair budget.</summary>
    public const string VerificationMaxRepairIterations = "sharpclaw.verification.maxRepairIterations";
    /// <summary>Explicitly enables restore for automatic verification.</summary>
    public const string VerificationAllowRestore = "sharpclaw.verification.allowRestore";
}
