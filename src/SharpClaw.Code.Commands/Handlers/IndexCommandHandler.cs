using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Refreshes and queries the persisted workspace knowledge index.
/// </summary>
public sealed class IndexCommandHandler(
    IWorkspaceIndexService workspaceIndexService,
    IWorkspaceSearchService workspaceSearchService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "index";

    /// <inheritdoc />
    public string Description => "Refreshes, inspects, and queries the workspace knowledge index.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);

        var refresh = new Command("refresh", "Refreshes the workspace index.");
        refresh.SetAction((parseResult, cancellationToken) => ExecuteRefreshAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(refresh);

        var stats = new Command("stats", "Shows workspace index status.");
        stats.SetAction((parseResult, cancellationToken) => ExecuteStatsAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(stats);

        var query = new Command("query", "Searches the workspace index.");
        var queryArgument = new Argument<string>("query") { Description = "Search query." };
        var limitOption = new Option<int?>("--limit") { Description = "Maximum number of hits to return." };
        query.Arguments.Add(queryArgument);
        query.Options.Add(limitOption);
        query.SetAction((parseResult, cancellationToken) => ExecuteQueryAsync(
            globalOptions.Resolve(parseResult),
            parseResult.GetValue(queryArgument) ?? string.Empty,
            parseResult.GetValue(limitOption),
            cancellationToken));
        command.Subcommands.Add(query);

        command.SetAction((parseResult, cancellationToken) => ExecuteStatsAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length > 0 && string.Equals(command.Arguments[0], "refresh", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRefreshAsync(context, cancellationToken);
        }

        if (command.Arguments.Length > 0 && string.Equals(command.Arguments[0], "query", StringComparison.OrdinalIgnoreCase))
        {
            var query = string.Join(' ', command.Arguments.Skip(1));
            return ExecuteQueryAsync(context, query, null, cancellationToken);
        }

        return ExecuteStatsAsync(context, cancellationToken);
    }

    private async Task<int> ExecuteRefreshAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await workspaceIndexService.RefreshAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        return await RenderAsync(context, result, $"Indexed {result.IndexedFileCount} file(s).", cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteStatsAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await workspaceIndexService.GetStatusAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        return await RenderAsync(
            context,
            result,
            result.RefreshedAtUtc is null
                ? "Workspace index has not been built yet."
                : $"Workspace index refreshed {result.RefreshedAtUtc:O}.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteQueryAsync(
        CommandExecutionContext context,
        string query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var result = await workspaceSearchService
            .SearchAsync(context.WorkingDirectory, new WorkspaceSearchRequest(query, limit), cancellationToken)
            .ConfigureAwait(false);
        return await RenderAsync(context, result, $"{result.Hits.Length} workspace hit(s).", cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RenderAsync<TPayload>(
        CommandExecutionContext context,
        TPayload payload,
        string message,
        CancellationToken cancellationToken)
    {
        var commandResult = new CommandResult(
            true,
            0,
            context.OutputFormat,
            message,
            JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.Options));
        await outputRendererDispatcher.RenderCommandResultAsync(commandResult, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
