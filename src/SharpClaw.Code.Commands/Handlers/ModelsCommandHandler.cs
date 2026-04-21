using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists the configured provider/model surface available to SharpClaw.
/// </summary>
public sealed class ModelsCommandHandler(
    IProviderCatalogService providerCatalogService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "models";

    /// <inheritdoc />
    public string Description => "Lists provider defaults, aliases, and authentication status.";

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
        var entries = await providerCatalogService.ListAsync(cancellationToken).ConfigureAwait(false);
        var payload = entries.ToList();

        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"{payload.Count} provider model surface(s).",
            JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.ListProviderModelCatalogEntry));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
