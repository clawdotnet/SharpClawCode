using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Surfaces durable usage totals for the current workspace.
/// </summary>
public sealed class UsageCommandHandler(
    IWorkspaceInsightsService workspaceInsightsService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "usage";

    /// <inheritdoc />
    public string Description => "Shows session and workspace token usage totals.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
        => ExecuteAsync(context, cancellationToken);

    private async Task<int> ExecuteAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var report = await workspaceInsightsService
            .BuildUsageReportAsync(context.WorkingDirectory, context.SessionId, cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Workspace total: {report.WorkspaceTotal.TotalTokens} tokens across {report.Sessions.Count} session(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.WorkspaceUsageReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
