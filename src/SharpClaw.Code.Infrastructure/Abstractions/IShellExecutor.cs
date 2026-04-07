using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Executes shell commands using the host platform shell.
/// </summary>
public interface IShellExecutor
{
    /// <summary>
    /// Executes a shell command asynchronously.
    /// </summary>
    /// <param name="command">The shell command text.</param>
    /// <param name="workingDirectory">The working directory, if any.</param>
    /// <param name="environmentVariables">Optional environment variable overrides.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process result.</returns>
    Task<ProcessRunResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        CancellationToken cancellationToken);
}
