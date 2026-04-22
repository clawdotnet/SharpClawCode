using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Agents.Models;

/// <summary>
/// Carries stable inputs for a single agent run.
/// </summary>
/// <param name="SessionId">The active session identifier.</param>
/// <param name="TurnId">The active turn identifier.</param>
/// <param name="Prompt">The user prompt or delegated task input.</param>
/// <param name="WorkingDirectory">The effective working directory.</param>
/// <param name="Model">The requested model alias or identifier.</param>
/// <param name="PermissionMode">The active permission mode.</param>
/// <param name="OutputFormat">The preferred output format.</param>
/// <param name="ToolExecutor">The mediated tool executor available to the agent.</param>
/// <param name="Metadata">Additional run metadata.</param>
/// <param name="ParentAgentId">The parent agent id for delegated runs, if any.</param>
/// <param name="DelegatedTask">The bounded delegated task contract, if any.</param>
/// <param name="PrimaryMode">Active build vs plan workflow mode for tool permission behavior.</param>
/// <param name="ToolMutationRecorder">Optional mutation recorder forwarded to tool executions.</param>
/// <param name="ConversationHistory">
/// Prior-turn messages assembled from session events. When non-empty these are prepended
/// to the provider request so the model has multi-turn context.
/// </param>
/// <param name="IsInteractive">Whether tool approvals can interact with the caller.</param>
/// <param name="ApprovalSettings">Optional bounded auto-approval settings forwarded to tool execution.</param>
public sealed record AgentRunContext(
    string SessionId,
    string TurnId,
    string Prompt,
    string WorkingDirectory,
    string Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    IToolExecutor ToolExecutor,
    IReadOnlyDictionary<string, string>? Metadata,
    string? ParentAgentId = null,
    DelegatedTaskContract? DelegatedTask = null,
    PrimaryMode PrimaryMode = PrimaryMode.Build,
    IToolMutationRecorder? ToolMutationRecorder = null,
    IReadOnlyList<ChatMessage>? ConversationHistory = null,
    bool IsInteractive = true,
    ApprovalSettings? ApprovalSettings = null);
