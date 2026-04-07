using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Runs a shell command through the infrastructure shell abstraction.
/// </summary>
public sealed class BashTool(IShellExecutor shellExecutor, IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "bash";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Execute a shell command inside the workspace.",
        ApprovalScope: ApprovalScope.ShellExecution,
        IsDestructive: true,
        RequiresApproval: true,
        InputTypeName: nameof(BashToolArguments),
        InputDescription: "JSON object with command plus optional working directory and env overrides.",
        Tags: ["shell", "bash", "command"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<BashToolArguments>(request);
        var pathResolver = new WorkspacePathResolver(pathService);
        var workingDirectory = pathResolver.ResolveWorkingDirectory(context, arguments.WorkingDirectory);
        var environmentVariables = arguments.EnvironmentVariables ?? context.EnvironmentVariables;
        var processResult = await shellExecutor
            .ExecuteAsync(arguments.Command, workingDirectory, environmentVariables, cancellationToken)
            .ConfigureAwait(false);
        var duration = (long)(processResult.CompletedAtUtc - processResult.StartedAtUtc).TotalMilliseconds;
        var payload = new BashToolResult(
            WorkingDirectory: workingDirectory,
            ExitCode: processResult.ExitCode,
            StandardOutput: processResult.StandardOutput,
            StandardError: processResult.StandardError);

        var textOutput = string.IsNullOrWhiteSpace(processResult.StandardOutput)
            ? processResult.StandardError
            : processResult.StandardOutput;

        return processResult.ExitCode == 0
            ? CreateSuccessResult(context, request, textOutput, payload, processResult.ExitCode, duration)
            : CreateFailureResult(context, request, processResult.StandardError, processResult.ExitCode, duration);
    }
}
