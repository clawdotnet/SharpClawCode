using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Surfaces estimated workspace cost based on persisted usage snapshots.
/// </summary>
public sealed class CostCommandHandler(
    IWorkspaceInsightsService workspaceInsightsService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "cost";

    /// <inheritdoc />
    public string Description => "Shows estimated usage cost for the current workspace.";

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
            .BuildCostReportAsync(context.WorkingDirectory, context.SessionId, cancellationToken)
            .ConfigureAwait(false);
        var total = report.WorkspaceEstimatedCostUsd.HasValue
            ? report.WorkspaceEstimatedCostUsd.Value.ToString("0.0000", CultureInfo.InvariantCulture)
            : "n/a";
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Workspace estimated cost: ${total} across {report.Sessions.Count} session(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.WorkspaceCostReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
