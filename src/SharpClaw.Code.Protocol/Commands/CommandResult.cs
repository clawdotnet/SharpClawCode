using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents a normalized command result payload.
/// </summary>
/// <param name="Succeeded">Indicates whether the command completed successfully.</param>
/// <param name="ExitCode">The command exit code.</param>
/// <param name="OutputFormat">The output format of the primary message or payload.</param>
/// <param name="Message">The primary command message.</param>
/// <param name="DataJson">An optional machine-readable JSON payload.</param>
public sealed record CommandResult(
    bool Succeeded,
    int ExitCode,
    OutputFormat OutputFormat,
    string Message,
    string? DataJson);
