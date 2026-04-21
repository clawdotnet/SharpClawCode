using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands.Models;

/// <summary>
/// Represents normalized global CLI options resolved for a command invocation.
/// </summary>
/// <param name="WorkingDirectory">The working directory for the invocation.</param>
/// <param name="Model">The selected model, if any.</param>
/// <param name="PermissionMode">The effective permission mode.</param>
/// <param name="OutputFormat">The requested output format.</param>
/// <param name="PrimaryMode">Build, plan, or spec workflow from global CLI options.</param>
/// <param name="SessionId">Optional explicit session id for prompts and session-scoped commands.</param>
/// <param name="AgentId">Optional explicit agent id for prompt execution.</param>
/// <param name="HostContext">Optional embedded-host identity and tenant/storage context.</param>
public sealed record CommandExecutionContext(
    string WorkingDirectory,
    string? Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    PrimaryMode PrimaryMode,
    string? SessionId = null,
    string? AgentId = null,
    RuntimeHostContext? HostContext = null)
{
    /// <summary>
    /// Converts the CLI command context into the runtime invocation context.
    /// </summary>
    /// <param name="isInteractive">Whether the current caller can participate in approval prompts.</param>
    /// <param name="primaryModeOverride">Optional primary-mode override.</param>
    /// <param name="agentIdOverride">Optional agent id override.</param>
    /// <returns>The runtime command context.</returns>
    public RuntimeCommandContext ToRuntimeCommandContext(
        bool isInteractive = true,
        PrimaryMode? primaryModeOverride = null,
        string? agentIdOverride = null)
        => new(
            WorkingDirectory,
            Model,
            PermissionMode,
            OutputFormat,
            primaryModeOverride ?? PrimaryMode,
            SessionId,
            agentIdOverride ?? AgentId,
            isInteractive,
            HostContext);
}
