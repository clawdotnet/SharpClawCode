namespace SharpClaw.Code.Infrastructure.Models;

/// <summary>
/// Represents a completed process execution result.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">The captured standard output.</param>
/// <param name="StandardError">The captured standard error.</param>
/// <param name="StartedAtUtc">The UTC timestamp when the process started.</param>
/// <param name="CompletedAtUtc">The UTC timestamp when the process completed.</param>
public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
