using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists the configured provider/model surface available to SharpClaw.
/// </summary>
public sealed class ModelsCommandHandler(
    IEnumerable<IModelProvider> modelProviders,
    IAuthFlowService authFlowService,
    IOptions<ProviderCatalogOptions> providerCatalogOptions,
    IOptions<AnthropicProviderOptions> anthropicOptions,
    IOptions<OpenAiCompatibleProviderOptions> openAiCompatibleOptions,
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
        var entries = new List<ProviderModelCatalogEntry>();
        var aliasesByProvider = providerCatalogOptions.Value.ModelAliases
            .GroupBy(static pair => pair.Value.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(pair => pair.Key).OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var provider in modelProviders.OrderBy(static provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            var auth = await authFlowService.GetStatusAsync(provider.ProviderName, cancellationToken).ConfigureAwait(false);
            entries.Add(
                new ProviderModelCatalogEntry(
                    provider.ProviderName,
                    ResolveDefaultModel(provider.ProviderName),
                    aliasesByProvider.TryGetValue(provider.ProviderName, out var aliases) ? aliases : [],
                    auth));
        }

        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"{entries.Count} provider model surface(s).",
            JsonSerializer.Serialize(entries, ProtocolJsonContext.Default.ListProviderModelCatalogEntry));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private string ResolveDefaultModel(string providerName)
        => string.Equals(providerName, anthropicOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
            ? anthropicOptions.Value.DefaultModel
            : string.Equals(providerName, openAiCompatibleOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
                ? openAiCompatibleOptions.Value.DefaultModel
                : "default";
}
