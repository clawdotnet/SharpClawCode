using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Provides the plugin management command surface.
/// </summary>
public sealed class PluginsCommandHandler(
    IPluginManager pluginManager,
    IPluginManifestImportService pluginManifestImportService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "plugins";

    /// <inheritdoc />
    public string Description => "Inspects and manages locally installed plugins.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildInstallCommand(globalOptions, isUpdate: false));
        command.Subcommands.Add(BuildInstallCommand(globalOptions, isUpdate: true));
        command.Subcommands.Add(BuildImportCommand(globalOptions));
        command.Subcommands.Add(BuildEnableCommand(globalOptions));
        command.Subcommands.Add(BuildDisableCommand(globalOptions));
        command.Subcommands.Add(BuildUninstallCommand(globalOptions));
        return command;
    }

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists locally tracked plugins.");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var plugins = await pluginManager.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            var result = new CommandResult(
                Succeeded: true,
                ExitCode: 0,
                OutputFormat: context.OutputFormat,
                Message: plugins.Count == 0
                    ? "No plugins are installed for this workspace."
                    : string.Join(Environment.NewLine, plugins.Select(plugin => $"{plugin.Descriptor.Id}: {plugin.State} ({plugin.Descriptor.Version})")),
                DataJson: JsonSerializer.Serialize(plugins.ToList(), ProtocolJsonContext.Default.ListLoadedPlugin));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private Command BuildInstallCommand(GlobalCliOptions globalOptions, bool isUpdate)
    {
        var command = new Command(isUpdate ? "update" : "install", isUpdate ? "Updates a plugin from a manifest file." : "Installs a plugin from a manifest file.");
        var manifestOption = new Option<string>("--manifest")
        {
            Required = true,
            Description = "The path to the plugin manifest JSON file."
        };

        command.Options.Add(manifestOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var manifestPath = parseResult.GetValue(manifestOption) ?? throw new InvalidOperationException("The --manifest option is required.");
            var request = await LoadInstallRequestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var plugin = isUpdate
                ? await pluginManager.UpdateAsync(context.WorkingDirectory, request, cancellationToken).ConfigureAwait(false)
                : await pluginManager.InstallAsync(context.WorkingDirectory, request, cancellationToken).ConfigureAwait(false);

            var result = new CommandResult(
                Succeeded: true,
                ExitCode: 0,
                OutputFormat: context.OutputFormat,
                Message: $"{(isUpdate ? "Updated" : "Installed")} plugin '{plugin.Descriptor.Id}' ({plugin.State}).",
                DataJson: JsonSerializer.Serialize(plugin, ProtocolJsonContext.Default.LoadedPlugin));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private Command BuildEnableCommand(GlobalCliOptions globalOptions)
        => BuildStateCommand("enable", "Enables an installed plugin.", globalOptions, pluginManager.EnableAsync);

    private Command BuildDisableCommand(GlobalCliOptions globalOptions)
        => BuildStateCommand("disable", "Disables an installed plugin.", globalOptions, pluginManager.DisableAsync);

    private Command BuildUninstallCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("uninstall", "Removes an installed plugin.");
        var idOption = new Option<string>("--id")
        {
            Required = true,
            Description = "The plugin identifier."
        };

        command.Options.Add(idOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var pluginId = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("The --id option is required.");
            await pluginManager.UninstallAsync(context.WorkingDirectory, pluginId, cancellationToken).ConfigureAwait(false);

            var result = new CommandResult(
                Succeeded: true,
                ExitCode: 0,
                OutputFormat: context.OutputFormat,
                Message: $"Uninstalled plugin '{pluginId}'.",
                DataJson: JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["id"] = pluginId },
                    ProtocolJsonContext.Default.DictionaryStringString));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private Command BuildImportCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("import", "Imports an external manifest into the local SharpClaw plugin format.");
        var manifestOption = new Option<string>("--manifest")
        {
            Required = true,
            Description = "The path to the external or SharpClaw plugin manifest JSON file."
        };
        var formatOption = new Option<string?>("--format")
        {
            Description = "Manifest format hint: auto, sharpclaw, or external."
        };
        var updateOption = new Option<bool>("--update")
        {
            Description = "Updates the plugin when already installed."
        };

        command.Options.Add(manifestOption);
        command.Options.Add(formatOption);
        command.Options.Add(updateOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var manifestPath = parseResult.GetValue(manifestOption) ?? throw new InvalidOperationException("The --manifest option is required.");
            var format = parseResult.GetValue(formatOption);
            var update = parseResult.GetValue(updateOption);
            var (request, importResult) = await pluginManifestImportService.ImportAsync(manifestPath, format, cancellationToken).ConfigureAwait(false);
            var plugin = update
                ? await pluginManager.UpdateAsync(context.WorkingDirectory, request, cancellationToken).ConfigureAwait(false)
                : await pluginManager.InstallAsync(context.WorkingDirectory, request, cancellationToken).ConfigureAwait(false);

            var message = importResult.Warnings.Length == 0
                ? $"Imported plugin '{plugin.Descriptor.Id}' from {importResult.SourceFormat} manifest."
                : $"Imported plugin '{plugin.Descriptor.Id}' from {importResult.SourceFormat} manifest with {importResult.Warnings.Length} warning(s).";
            var result = new CommandResult(
                true,
                0,
                context.OutputFormat,
                message,
                JsonSerializer.Serialize(importResult, ProtocolJsonContext.Default.ImportedPluginManifestResult));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        });

        return command;
    }

    private Command BuildStateCommand(
        string name,
        string description,
        GlobalCliOptions globalOptions,
        Func<string, string, CancellationToken, Task<SharpClaw.Code.Protocol.Models.LoadedPlugin>> action)
    {
        var command = new Command(name, description);
        var idOption = new Option<string>("--id")
        {
            Required = true,
            Description = "The plugin identifier."
        };

        command.Options.Add(idOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var pluginId = parseResult.GetValue(idOption) ?? throw new InvalidOperationException("The --id option is required.");
            var plugin = await action(context.WorkingDirectory, pluginId, cancellationToken).ConfigureAwait(false);
            var result = new CommandResult(
                Succeeded: plugin.State is not Protocol.Enums.PluginLifecycleState.Faulted,
                ExitCode: plugin.State is Protocol.Enums.PluginLifecycleState.Faulted ? 1 : 0,
                OutputFormat: context.OutputFormat,
                Message: $"{plugin.Descriptor.Id}: {plugin.State}{(string.IsNullOrWhiteSpace(plugin.FailureReason) ? string.Empty : $" - {plugin.FailureReason}")}",
                DataJson: JsonSerializer.Serialize(plugin, ProtocolJsonContext.Default.LoadedPlugin));
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });

        return command;
    }

    private static async Task<PluginInstallRequest> LoadInstallRequestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Manifest '{manifestPath}' could not be parsed.");
        return new PluginInstallRequest(manifest, PackageContent: null);
    }
}
