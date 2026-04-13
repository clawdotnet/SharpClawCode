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
/// Lists the effective agent catalog and manages the REPL agent override.
/// </summary>
public sealed class AgentsCommandHandler(
    IAgentCatalogService agentCatalogService,
    ReplInteractionState replInteractionState,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "agents";

    /// <inheritdoc />
    public string Description => "Lists agents and selects the active REPL agent override.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);

        var list = new Command("list", "Lists the effective agent catalog.");
        list.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(list);

        var use = new Command("use", "Sets the current process REPL agent override.");
        var idOption = new Option<string>("--id") { Required = true, Description = "Agent id to activate." };
        use.Options.Add(idOption);
        use.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var id = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("--id is required.");
            return await ExecuteUseAsync(id, context, cancellationToken).ConfigureAwait(false);
        });
        command.Subcommands.Add(use);

        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "use", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteUseAsync(command.Arguments[1], context, cancellationToken);
        }

        return ExecuteListAsync(context, cancellationToken);
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var agents = await agentCatalogService.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var message = replInteractionState.AgentIdOverride is null
            ? $"{agents.Count} agent(s)."
            : $"{agents.Count} agent(s). Active REPL agent override: {replInteractionState.AgentIdOverride}.";
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            message,
            JsonSerializer.Serialize(agents, ProtocolJsonContext.Default.ListAgentCatalogEntry));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteUseAsync(string agentId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var agents = await agentCatalogService.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        if (!agents.Any(agent => string.Equals(agent.Id, agentId, StringComparison.OrdinalIgnoreCase)))
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(false, 1, context.OutputFormat, $"Unknown agent '{agentId}'.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }

        replInteractionState.AgentIdOverride = agentId;
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Active REPL agent set to {agentId}.", null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
