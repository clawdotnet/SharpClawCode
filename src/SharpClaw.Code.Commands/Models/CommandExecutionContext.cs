using SharpClaw.Code.Protocol.Enums;

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
public sealed record CommandExecutionContext(
    string WorkingDirectory,
    string? Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    PrimaryMode PrimaryMode,
    string? SessionId = null);
