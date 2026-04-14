using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents a request to execute a prompt through the runtime.
/// </summary>
/// <param name="Prompt">The prompt content to execute.</param>
/// <param name="SessionId">The existing session identifier, if resuming.</param>
/// <param name="WorkingDirectory">The working directory to bind for the execution.</param>
/// <param name="PermissionMode">The permission mode for the execution.</param>
/// <param name="OutputFormat">The desired output format.</param>
/// <param name="Metadata">Additional machine-readable request metadata.</param>
/// <param name="PrimaryMode">When null, the runtime uses session metadata or defaults to <see cref="PrimaryMode.Build"/>.</param>
/// <param name="AgentId">When non-null, selects the registered agent id for the turn.</param>
/// <param name="DelegatedTask">When non-null, supplies the bounded contract for <c>sub-agent-worker</c> runs.</param>
/// <param name="IsInteractive">Whether the caller can participate in approval prompts.</param>
public sealed record RunPromptRequest(
    string Prompt,
    string? SessionId,
    string? WorkingDirectory,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    Dictionary<string, string>? Metadata,
    PrimaryMode? PrimaryMode = null,
    string? AgentId = null,
    DelegatedTaskContract? DelegatedTask = null,
    bool IsInteractive = true);
