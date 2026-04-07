namespace SharpClaw.Code.Infrastructure.Models;

/// <summary>
/// Describes a process execution request.
/// </summary>
/// <param name="FileName">The executable or command to run.</param>
/// <param name="Arguments">The command-line arguments.</param>
/// <param name="WorkingDirectory">The working directory, if any.</param>
/// <param name="EnvironmentVariables">Optional environment variable overrides.</param>
public sealed record ProcessRunRequest(
    string FileName,
    string[] Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables);
