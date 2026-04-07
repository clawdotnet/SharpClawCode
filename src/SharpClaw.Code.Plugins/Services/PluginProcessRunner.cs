using System.ComponentModel;
using System.Diagnostics;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Services;

/// <summary>
/// Executes plugin entry points through the shared process runner abstraction.
/// </summary>
public sealed class PluginProcessRunner(IProcessRunner processRunner) : IPluginProcessRunner
{
    /// <inheritdoc />
    public async Task<PluginLoadResult> LoadAsync(PluginManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        try
        {
            var result = await processRunner.RunAsync(
                new ProcessRunRequest(
                    FileName: manifest.EntryPoint,
                    Arguments: [.. manifest.Arguments ?? [], "--sharpclaw-plugin-check"],
                    WorkingDirectory: null,
                    EnvironmentVariables: null),
                cancellationToken).ConfigureAwait(false);

            return result.ExitCode == 0
                ? new PluginLoadResult(true, "out-of-process", null)
                : new PluginLoadResult(false, "out-of-process", string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError, PluginFailureKind.Load);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or FileNotFoundException)
        {
            return new PluginLoadResult(false, "out-of-process", ex.Message, PluginFailureKind.Load);
        }
    }

    /// <inheritdoc />
    public async Task<PluginExecutionResult> ExecuteAsync(
        PluginManifest manifest,
        PluginToolDescriptor tool,
        ToolExecutionRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(request);

        var result = await processRunner.RunAsync(
            new ProcessRunRequest(
                FileName: manifest.EntryPoint,
                Arguments:
                [
                    .. manifest.Arguments ?? [],
                    "--sharpclaw-tool",
                    tool.Name,
                    "--sharpclaw-input",
                    request.ArgumentsJson
                ],
                WorkingDirectory: request.WorkingDirectory ?? workspaceRoot,
                EnvironmentVariables: new Dictionary<string, string?>
                {
                    ["SHARPCLAW_WORKSPACE_ROOT"] = workspaceRoot
                }),
            cancellationToken).ConfigureAwait(false);

        return new PluginExecutionResult(
            Succeeded: result.ExitCode == 0,
            Output: string.IsNullOrWhiteSpace(result.StandardOutput) ? null : result.StandardOutput.Trim(),
            Error: string.IsNullOrWhiteSpace(result.StandardError) ? null : result.StandardError.Trim(),
            ExitCode: result.ExitCode,
            StructuredOutputJson: null,
            OutputFormat: OutputFormat.Text);
    }
}
