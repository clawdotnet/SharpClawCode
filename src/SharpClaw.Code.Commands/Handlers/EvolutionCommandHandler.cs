using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Analyzes and manages guided self-evolution proposals.
/// </summary>
public sealed class EvolutionCommandHandler(
    IEvolutionProposalService evolutionProposalService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "evolution";

    /// <inheritdoc />
    public string Description => "Analyzes workspace signals, stores proposals, and applies or rejects them.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        var analyze = new Command("analyze", "Refreshes durable evolution proposals from workspace signals.");
        analyze.SetAction((parseResult, cancellationToken) => ExecuteAnalyzeAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(analyze);

        var list = new Command("list", "Lists evolution proposals.");
        list.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(list);

        var show = CreateIdCommand("show", "Shows one evolution proposal.", globalOptions, ExecuteShowAsync);
        command.Subcommands.Add(show);

        var apply = CreateIdCommand("apply", "Applies one evolution proposal.", globalOptions, ExecuteApplyAsync);
        command.Subcommands.Add(apply);

        var reject = CreateIdCommand("reject", "Rejects one evolution proposal.", globalOptions, ExecuteRejectAsync);
        command.Subcommands.Add(reject);

        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0 || string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteListAsync(context, cancellationToken);
        }

        if (string.Equals(command.Arguments[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteAnalyzeAsync(context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "show", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteShowAsync(command.Arguments[1], context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "apply", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteApplyAsync(command.Arguments[1], context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "reject", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRejectAsync(command.Arguments[1], context, cancellationToken);
        }

        return RenderAsync("Usage: /evolution [analyze|list|show|apply|reject <proposalId>]", context, false, cancellationToken);
    }

    private Command CreateIdCommand(
        string name,
        string description,
        GlobalCliOptions globalOptions,
        Func<string, CommandExecutionContext, CancellationToken, Task<int>> action)
    {
        var command = new Command(name, description);
        var idOption = new Option<string>("--id") { Required = true, Description = "Evolution proposal id." };
        command.Options.Add(idOption);
        command.SetAction((parseResult, cancellationToken) => action(
            parseResult.GetValue(idOption) ?? throw new InvalidOperationException("--id is required."),
            globalOptions.Resolve(parseResult),
            cancellationToken));
        return command;
    }

    private async Task<int> ExecuteAnalyzeAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var proposals = await evolutionProposalService.AnalyzeAsync(context.WorkingDirectory, context.SessionId, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{proposals.Count} evolution proposal(s).", JsonSerializer.Serialize(proposals.ToList(), ProtocolJsonContext.Default.ListEvolutionProposal)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var proposals = await evolutionProposalService.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{proposals.Count} evolution proposal(s).", JsonSerializer.Serialize(proposals.ToList(), ProtocolJsonContext.Default.ListEvolutionProposal)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteShowAsync(string proposalId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var proposal = await evolutionProposalService.GetAsync(context.WorkingDirectory, proposalId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return await RenderAsync($"Evolution proposal '{proposalId}' was not found.", context, false, cancellationToken).ConfigureAwait(false);
        }

        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{proposal.Id}: {proposal.Title}", JsonSerializer.Serialize(proposal, ProtocolJsonContext.Default.EvolutionProposal)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteApplyAsync(string proposalId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var proposal = await evolutionProposalService
            .ApplyAsync(context.WorkingDirectory, proposalId, context.ToRuntimeCommandContext(), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Applied evolution proposal '{proposal.Id}' ({proposal.Category}).", JsonSerializer.Serialize(proposal, ProtocolJsonContext.Default.EvolutionProposal)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteRejectAsync(string proposalId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var proposal = await evolutionProposalService
            .RejectAsync(context.WorkingDirectory, proposalId, context.AgentId ?? "cli", cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Rejected evolution proposal '{proposal.Id}'.", JsonSerializer.Serialize(proposal, ProtocolJsonContext.Default.EvolutionProposal)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RenderAsync(string message, CommandExecutionContext context, bool success, CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }
}
