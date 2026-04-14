using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Surfaces durable workspace execution counts.
/// </summary>
public sealed class StatsCommandHandler(
    IWorkspaceInsightsService workspaceInsightsService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "stats";

    /// <inheritdoc />
    public string Description => "Shows turn, tool, provider, share, and todo counts.";

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
            .BuildStatsReportAsync(context.WorkingDirectory, context.SessionId, cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"{report.SessionCount} session(s), {report.TurnCompletedCount} completed turn(s), {report.ToolExecutionCount} tool execution(s), {report.ActiveTodoCount} active todo(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.WorkspaceStatsReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
