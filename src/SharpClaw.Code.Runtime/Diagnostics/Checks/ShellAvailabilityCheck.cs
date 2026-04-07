using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Verifies a trivial shell invocation works in the workspace.
/// </summary>
public sealed class ShellAvailabilityCheck(IProcessRunner processRunner) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "shell.exec";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        try
        {
            ProcessRunRequest request = OperatingSystem.IsWindows()
                ? new ProcessRunRequest(FileName: "cmd.exe", Arguments: ["/c", "echo", "sharpclaw_ok"], WorkingDirectory: context.NormalizedWorkspacePath, EnvironmentVariables: null)
                : new ProcessRunRequest(FileName: "/bin/sh", Arguments: ["-c", "echo sharpclaw_ok"], WorkingDirectory: context.NormalizedWorkspacePath, EnvironmentVariables: null);

            var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return new OperationalCheckItem(
                    Id,
                    OperationalCheckStatus.Warn,
                    "Shell probe exited non-zero.",
                    result.StandardError);
            }

            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Ok,
                "Shell probe succeeded.",
                result.StandardOutput.Trim());
        }
        catch (Exception exception)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Warn,
                "Shell probe could not run.",
                exception.Message);
        }
    }
}
