using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents a request to execute a slash command through the runtime.
/// </summary>
/// <param name="CommandName">The slash command name without transport-specific prefixes.</param>
/// <param name="Arguments">The command arguments in order.</param>
/// <param name="SessionId">The existing session identifier, if resuming.</param>
/// <param name="WorkingDirectory">The working directory to bind for the execution.</param>
/// <param name="OutputFormat">The desired output format.</param>
/// <param name="Metadata">Additional machine-readable request metadata.</param>
public sealed record RunSlashCommandRequest(
    string CommandName,
    string[] Arguments,
    string? SessionId,
    string? WorkingDirectory,
    OutputFormat OutputFormat,
    Dictionary<string, string>? Metadata);
