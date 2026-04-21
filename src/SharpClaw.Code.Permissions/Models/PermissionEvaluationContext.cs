using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Carries runtime state and policy inputs needed to evaluate a permission request.
/// </summary>
/// <param name="SessionId">The active session identifier.</param>
/// <param name="WorkspaceRoot">The workspace root that constrains file access.</param>
/// <param name="WorkingDirectory">The effective working directory for relative operations.</param>
/// <param name="PermissionMode">The active permission mode.</param>
/// <param name="AllowedTools">The explicitly allowed tools, if the caller restricts tool execution.</param>
/// <param name="AllowDangerousBypass">Indicates whether dangerous shell approval can be bypassed explicitly.</param>
/// <param name="IsInteractive">Indicates whether the current caller can participate in approval prompts.</param>
/// <param name="SourceKind">The caller kind that initiated tool execution.</param>
/// <param name="SourceName">The caller name, such as a plugin or MCP server id.</param>
/// <param name="TrustedPluginNames">The trusted plugin names for the current session.</param>
/// <param name="TrustedMcpServerNames">The trusted MCP server names for the current session.</param>
/// <param name="ToolOriginatingPluginId">The plugin id when executing a plugin-surfaced tool; otherwise null.</param>
/// <param name="ToolOriginatingPluginTrust">The manifest trust tier for the originating plugin tool, if any.</param>
/// <param name="PrimaryMode">Build vs plan workflow; plan mode blocks mutating tools.</param>
/// <param name="TenantId">The active tenant identifier, when one is bound to the host context.</param>
public sealed record PermissionEvaluationContext(
    string SessionId,
    string WorkspaceRoot,
    string WorkingDirectory,
    PermissionMode PermissionMode,
    IReadOnlyCollection<string>? AllowedTools,
    bool AllowDangerousBypass,
    bool IsInteractive,
    PermissionRequestSourceKind SourceKind,
    string? SourceName,
    IReadOnlyCollection<string>? TrustedPluginNames,
    IReadOnlyCollection<string>? TrustedMcpServerNames,
    string? ToolOriginatingPluginId = null,
    PluginTrustLevel? ToolOriginatingPluginTrust = null,
    PrimaryMode PrimaryMode = PrimaryMode.Build,
    string? TenantId = null);
