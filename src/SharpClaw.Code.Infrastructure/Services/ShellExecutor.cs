using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Executes shell commands through the host platform shell.
/// </summary>
public sealed class ShellExecutor(IProcessRunner processRunner) : IShellExecutor
{
    /// <inheritdoc />
    public Task<ProcessRunResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var request = OperatingSystem.IsWindows()
            ? new ProcessRunRequest("cmd.exe", ["/c", command], workingDirectory, environmentVariables)
            : new ProcessRunRequest("/bin/sh", ["-lc", command], workingDirectory, environmentVariables);

        return processRunner.RunAsync(request, cancellationToken);
    }
}
