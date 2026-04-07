using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Tools.Models;

/// <summary>
/// Carries stable context needed for a single tool execution.
/// </summary>
/// <param name="SessionId">The active session identifier.</param>
/// <param name="TurnId">The active turn identifier.</param>
/// <param name="WorkspaceRoot">The workspace root that constrains file access.</param>
/// <param name="WorkingDirectory">The effective working directory for relative operations.</param>
/// <param name="PermissionMode">The active permission mode.</param>
/// <param name="OutputFormat">The preferred output format.</param>
/// <param name="EnvironmentVariables">Optional environment variables for subprocess tools.</param>
/// <param name="AllowedTools">The explicitly allowed tools, if tool execution is restricted.</param>
/// <param name="AllowDangerousBypass">Indicates whether dangerous shell approval can be bypassed explicitly.</param>
/// <param name="IsInteractive">Indicates whether approval prompts can interact with the caller.</param>
/// <param name="SourceKind">The caller category that initiated the request.</param>
/// <param name="SourceName">The caller name, such as a plugin or MCP server identifier.</param>
/// <param name="TrustedPluginNames">The trusted plugin names for the current session.</param>
/// <param name="TrustedMcpServerNames">The trusted MCP server names for the current session.</param>
/// <param name="PrimaryMode">Workflow mode forwarded to permission evaluation.</param>
/// <param name="MutationRecorder">Optional recorder for reversible workspace file mutations.</param>
public sealed record ToolExecutionContext(
    string SessionId,
    string TurnId,
    string WorkspaceRoot,
    string WorkingDirectory,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables,
    IReadOnlyCollection<string>? AllowedTools = null,
    bool AllowDangerousBypass = false,
    bool IsInteractive = true,
    PermissionRequestSourceKind SourceKind = PermissionRequestSourceKind.Runtime,
    string? SourceName = null,
    IReadOnlyCollection<string>? TrustedPluginNames = null,
    IReadOnlyCollection<string>? TrustedMcpServerNames = null,
    PrimaryMode PrimaryMode = PrimaryMode.Build,
    IToolMutationRecorder? MutationRecorder = null);
