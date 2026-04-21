using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Controls how sessions are shared from SharpClaw.
/// </summary>
public enum ShareMode
{
    /// <summary>Sessions are shared only when explicitly requested.</summary>
    Manual,

    /// <summary>New sessions are shared automatically after successful turns.</summary>
    Auto,

    /// <summary>Sharing is disabled for the workspace.</summary>
    Disabled,
}

/// <summary>
/// Declares the severity of a workspace diagnostic item.
/// </summary>
public enum WorkspaceDiagnosticSeverity
{
    /// <summary>Informational finding.</summary>
    Info,

    /// <summary>Warning finding.</summary>
    Warning,

    /// <summary>Error finding.</summary>
    Error,
}

/// <summary>
/// Declares the lifecycle trigger for a configured runtime hook.
/// </summary>
public enum HookTriggerKind
{
    /// <summary>Fires when a turn starts.</summary>
    TurnStarted,

    /// <summary>Fires when a turn completes successfully.</summary>
    TurnCompleted,

    /// <summary>Fires when a tool starts.</summary>
    ToolStarted,

    /// <summary>Fires when a tool completes.</summary>
    ToolCompleted,

    /// <summary>Fires when a share snapshot is created.</summary>
    ShareCreated,

    /// <summary>Fires when a share snapshot is removed.</summary>
    ShareRemoved,

    /// <summary>Fires when the HTTP server finishes handling a request.</summary>
    ServerRequestCompleted,
}

/// <summary>
/// Scope for a tracked todo item.
/// </summary>
public enum TodoScope
{
    /// <summary>Task belongs to a single session.</summary>
    Session,

    /// <summary>Task belongs to the workspace.</summary>
    Workspace,
}

/// <summary>
/// Lifecycle state for a tracked todo item.
/// </summary>
public enum TodoStatus
{
    /// <summary>Task is open and not yet started.</summary>
    Open,

    /// <summary>Task is actively being worked.</summary>
    InProgress,

    /// <summary>Task is blocked on another dependency.</summary>
    Blocked,

    /// <summary>Task is completed.</summary>
    Done,
}

/// <summary>
/// Describes a configured agent override or workspace-defined specialist agent.
/// </summary>
/// <param name="Id">Stable agent id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional human-readable description.</param>
/// <param name="BaseAgentId">Optional built-in agent id this definition inherits from.</param>
/// <param name="Model">Optional default model override.</param>
/// <param name="PrimaryMode">Optional primary-mode default for the agent.</param>
/// <param name="AllowedTools">Optional exact tool allowlist resolved before tool execution.</param>
/// <param name="InstructionAppendix">Optional extra system instructions appended to the base agent instructions.</param>
/// <param name="IsDefault">Whether the definition should be selected by default when no agent is requested.</param>
public sealed record ConfiguredAgentDefinition(
    string Id,
    string Name,
    string? Description,
    string? BaseAgentId,
    string? Model,
    PrimaryMode? PrimaryMode,
    string[]? AllowedTools,
    string? InstructionAppendix,
    bool IsDefault = false);

/// <summary>
/// Declares an LSP-style diagnostics source configured for the workspace.
/// </summary>
/// <param name="Id">Stable source id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Command">Executable used to start or query diagnostics.</param>
/// <param name="Arguments">Command arguments.</param>
/// <param name="Enabled">Whether the source is enabled.</param>
/// <param name="Environment">Optional environment variable overrides.</param>
public sealed record ConfiguredLspServerDefinition(
    string Id,
    string Name,
    string Command,
    string[] Arguments,
    bool Enabled,
    Dictionary<string, string>? Environment = null);

/// <summary>
/// Declares an external hook process tied to a runtime trigger.
/// </summary>
/// <param name="Name">Stable hook name.</param>
/// <param name="Trigger">Runtime trigger this hook listens to.</param>
/// <param name="Command">Executable to invoke.</param>
/// <param name="Arguments">Arguments to supply.</param>
/// <param name="Enabled">Whether the hook is enabled.</param>
public sealed record HookDefinition(
    string Name,
    HookTriggerKind Trigger,
    string Command,
    string[] Arguments,
    bool Enabled = true);

/// <summary>
/// Configures the embedded SharpClaw HTTP server.
/// </summary>
/// <param name="Host">Bind host.</param>
/// <param name="Port">Bind port.</param>
/// <param name="PublicBaseUrl">Optional externally reachable base URL used for share links.</param>
/// <param name="ApprovalAuth">Optional approval-auth configuration for HTTP and admin callers.</param>
public sealed record SharpClawServerOptions(
    string Host,
    int Port,
    string? PublicBaseUrl = null,
    SharpClawApprovalAuthOptions? ApprovalAuth = null);

/// <summary>
/// Configures browser-based connection entry points for providers or MCP servers.
/// </summary>
/// <param name="Target">Stable target id such as a provider or MCP server name.</param>
/// <param name="DisplayName">Human-readable target name.</param>
/// <param name="Url">Connection URL to open.</param>
public sealed record ConnectLinkDefinition(
    string Target,
    string DisplayName,
    string Url);

/// <summary>
/// User/workspace configuration document loaded from JSONC.
/// </summary>
/// <param name="ShareMode">Configured share mode.</param>
/// <param name="Server">Embedded server options.</param>
/// <param name="DefaultAgentId">Optional default agent id.</param>
/// <param name="Agents">Configured agent catalog entries.</param>
/// <param name="LspServers">Configured diagnostics sources.</param>
/// <param name="Hooks">Configured runtime hooks.</param>
/// <param name="ConnectLinks">Optional auth/connect entry points.</param>
public sealed record SharpClawConfigDocument(
    ShareMode? ShareMode,
    SharpClawServerOptions? Server,
    string? DefaultAgentId,
    List<ConfiguredAgentDefinition>? Agents,
    List<ConfiguredLspServerDefinition>? LspServers,
    List<HookDefinition>? Hooks,
    List<ConnectLinkDefinition>? ConnectLinks);

/// <summary>
/// Materialized configuration snapshot after user/workspace precedence is applied.
/// </summary>
/// <param name="WorkspaceRoot">Workspace the config was resolved for.</param>
/// <param name="UserConfigPath">User config path when present.</param>
/// <param name="WorkspaceConfigPath">Workspace config path when present.</param>
/// <param name="Document">Merged configuration document.</param>
public sealed record SharpClawConfigSnapshot(
    string WorkspaceRoot,
    string? UserConfigPath,
    string? WorkspaceConfigPath,
    SharpClawConfigDocument Document);

/// <summary>
/// Describes a resolved agent visible to commands and prompt execution.
/// </summary>
/// <param name="Id">Stable agent id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Description.</param>
/// <param name="BaseAgentId">Backing built-in agent id.</param>
/// <param name="Model">Default model, if any.</param>
/// <param name="PrimaryMode">Primary-mode default, if any.</param>
/// <param name="AllowedTools">Resolved allowed-tool list, if any.</param>
/// <param name="InstructionAppendix">Additional agent instructions, if any.</param>
/// <param name="IsBuiltIn">Whether this entry maps directly to a built-in agent.</param>
/// <param name="IsDefault">Whether this is the default prompt agent.</param>
public sealed record AgentCatalogEntry(
    string Id,
    string Name,
    string Description,
    string BaseAgentId,
    string? Model,
    PrimaryMode? PrimaryMode,
    string[]? AllowedTools,
    string? InstructionAppendix,
    bool IsBuiltIn,
    bool IsDefault);

/// <summary>
/// Summarizes one provider/model surface entry for the CLI and JSON APIs.
/// </summary>
/// <param name="ProviderName">Provider name.</param>
/// <param name="DefaultModel">Provider default model.</param>
/// <param name="Aliases">Aliases that resolve to this provider.</param>
/// <param name="AuthStatus">Current authentication state.</param>
/// <param name="SupportsToolCalls">Whether tool calling is supported.</param>
/// <param name="SupportsEmbeddings">Whether embeddings are supported.</param>
/// <param name="AvailableModels">Discovered models for the provider.</param>
/// <param name="LocalRuntimeProfiles">Configured local runtime profiles, if any.</param>
public sealed record ProviderModelCatalogEntry(
    string ProviderName,
    string DefaultModel,
    string[] Aliases,
    AuthStatus AuthStatus,
    bool SupportsToolCalls = true,
    bool SupportsEmbeddings = false,
    ProviderDiscoveredModel[]? AvailableModels = null,
    LocalRuntimeProfileSummary[]? LocalRuntimeProfiles = null);

/// <summary>
/// Summarizes a browser-connectable target.
/// </summary>
/// <param name="Target">Stable target id.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Kind">Target kind such as provider or mcp.</param>
/// <param name="IsAuthenticated">Whether the target is currently authenticated.</param>
/// <param name="ConnectUrl">Optional browser URL.</param>
/// <param name="ExpiresAtUtc">Optional authentication expiry timestamp.</param>
/// <param name="StatusDetail">Optional status detail shown alongside authentication state.</param>
public sealed record ConnectTargetStatus(
    string Target,
    string DisplayName,
    string Kind,
    bool IsAuthenticated,
    string? ConnectUrl,
    DateTimeOffset? ExpiresAtUtc = null,
    string? StatusDetail = null);

/// <summary>
/// One workspace diagnostic item surfaced to prompts, CLI, and APIs.
/// </summary>
/// <param name="Severity">Diagnostic severity.</param>
/// <param name="Code">Diagnostic code, if known.</param>
/// <param name="Message">Human-readable diagnostic message.</param>
/// <param name="FilePath">Absolute or workspace-relative file path, if known.</param>
/// <param name="Line">1-based line number, if known.</param>
/// <param name="Column">1-based column number, if known.</param>
/// <param name="Source">Originating diagnostics source.</param>
public sealed record WorkspaceDiagnosticItem(
    WorkspaceDiagnosticSeverity Severity,
    string? Code,
    string Message,
    string? FilePath,
    int? Line,
    int? Column,
    string Source);

/// <summary>
/// Workspace diagnostics snapshot used by prompts and operational output.
/// </summary>
/// <param name="WorkspaceRoot">Workspace root.</param>
/// <param name="GeneratedAtUtc">Snapshot timestamp.</param>
/// <param name="ConfiguredLspServers">Configured diagnostics sources.</param>
/// <param name="Diagnostics">Collected diagnostics.</param>
public sealed record WorkspaceDiagnosticsSnapshot(
    string WorkspaceRoot,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ConfiguredLspServerDefinition> ConfiguredLspServers,
    IReadOnlyList<WorkspaceDiagnosticItem> Diagnostics);

/// <summary>
/// Request payload accepted by the embedded SharpClaw HTTP prompt endpoint.
/// </summary>
/// <param name="Prompt">Prompt text to execute.</param>
/// <param name="SessionId">Optional explicit session id.</param>
/// <param name="Model">Optional model override.</param>
/// <param name="PermissionMode">Optional permission override.</param>
/// <param name="OutputFormat">Optional output format override.</param>
/// <param name="PrimaryMode">Optional primary mode override.</param>
/// <param name="AgentId">Optional agent override.</param>
/// <param name="TenantId">Optional tenant override for embedded hosts.</param>
public sealed record ServerPromptRequest(
    string Prompt,
    string? SessionId,
    string? Model,
    PermissionMode? PermissionMode,
    OutputFormat? OutputFormat,
    PrimaryMode? PrimaryMode,
    string? AgentId,
    string? TenantId);

/// <summary>
/// Metadata for a shared session snapshot.
/// </summary>
/// <param name="ShareId">Stable share id.</param>
/// <param name="SessionId">Shared session id.</param>
/// <param name="WorkspaceRoot">Workspace root.</param>
/// <param name="Url">Resolved share URL.</param>
/// <param name="Mode">Share mode active when the snapshot was created.</param>
/// <param name="CreatedAtUtc">Share creation timestamp.</param>
public sealed record ShareSessionRecord(
    string ShareId,
    string SessionId,
    string WorkspaceRoot,
    string Url,
    ShareMode Mode,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Durable sanitized payload exposed through a share link.
/// </summary>
/// <param name="Record">Share metadata.</param>
/// <param name="Session">Shared session snapshot.</param>
/// <param name="Events">Shared runtime events.</param>
public sealed record SharedSessionSnapshot(
    ShareSessionRecord Record,
    ConversationSession Session,
    RuntimeEvent[] Events);

/// <summary>
/// Usage summary for one session.
/// </summary>
public sealed record SessionUsageReport(
    string SessionId,
    string Title,
    bool IsAttached,
    bool IsCurrent,
    UsageSnapshot Usage);

/// <summary>
/// Workspace usage report.
/// </summary>
public sealed record WorkspaceUsageReport(
    string WorkspaceRoot,
    string? CurrentSessionId,
    string? AttachedSessionId,
    UsageSnapshot WorkspaceTotal,
    IReadOnlyList<SessionUsageReport> Sessions);

/// <summary>
/// Cost summary for one session.
/// </summary>
public sealed record SessionCostReport(
    string SessionId,
    string Title,
    bool IsAttached,
    bool IsCurrent,
    decimal? EstimatedCostUsd);

/// <summary>
/// Workspace cost report.
/// </summary>
public sealed record WorkspaceCostReport(
    string WorkspaceRoot,
    string? CurrentSessionId,
    string? AttachedSessionId,
    decimal? WorkspaceEstimatedCostUsd,
    IReadOnlyList<SessionCostReport> Sessions);

/// <summary>
/// Workspace execution stats report.
/// </summary>
public sealed record WorkspaceStatsReport(
    string WorkspaceRoot,
    string? CurrentSessionId,
    string? AttachedSessionId,
    int SessionCount,
    int TurnStartedCount,
    int TurnCompletedCount,
    int ToolExecutionCount,
    int ProviderRequestCount,
    int SharedSessionCount,
    int ActiveTodoCount);

/// <summary>
/// Configured hook status.
/// </summary>
public sealed record HookStatusRecord(
    string Name,
    HookTriggerKind Trigger,
    string Command,
    string[] Arguments,
    bool Enabled,
    DateTimeOffset? LastTestedAtUtc = null,
    bool? LastTestSucceeded = null,
    string? LastTestMessage = null);

/// <summary>
/// Result of testing a configured hook.
/// </summary>
public sealed record HookTestResult(
    string Name,
    HookTriggerKind Trigger,
    bool Succeeded,
    string Message,
    DateTimeOffset TestedAtUtc);

/// <summary>
/// Skill inspection payload.
/// </summary>
public sealed record SkillInspectionRecord(
    SkillDefinition Definition,
    string PromptTemplate,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Normalized imported plugin manifest payload.
/// </summary>
public sealed record ImportedPluginManifestResult(
    string SourceFormat,
    string PluginId,
    string Name,
    string Version,
    string EntryPoint,
    int ToolCount,
    string[] Warnings);

/// <summary>
/// Single durable todo item.
/// </summary>
public sealed record TodoItem(
    string Id,
    string Title,
    TodoStatus Status,
    TodoScope Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? OwnerAgentId,
    string? LinkedSessionId);

/// <summary>
/// Snapshot of workspace and session tasks.
/// </summary>
public sealed record TodoSnapshot(
    string WorkspaceRoot,
    string? SessionId,
    IReadOnlyList<TodoItem> SessionTodos,
    IReadOnlyList<TodoItem> WorkspaceTodos);
