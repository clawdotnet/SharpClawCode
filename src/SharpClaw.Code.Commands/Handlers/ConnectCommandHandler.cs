using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists and opens browser-based provider or external connection entry points.
/// </summary>
public sealed class ConnectCommandHandler(
    ISharpClawConfigService sharpClawConfigService,
    IEnumerable<SharpClaw.Code.Providers.Abstractions.IModelProvider> modelProviders,
    IAuthFlowService authFlowService,
    IPathService pathService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "connect";

    /// <inheritdoc />
    public string Description => "Lists connection targets and opens configured browser flows.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);

        var list = new Command("list", "Lists configured connection targets.");
        list.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(list);

        var open = new Command("open", "Opens a configured browser connection target.");
        var targetOption = new Option<string>("--target") { Required = true, Description = "Target id to open." };
        open.Options.Add(targetOption);
        open.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var target = parseResult.GetValue(targetOption) ?? throw new InvalidOperationException("--target is required.");
            return await ExecuteOpenAsync(target, context, cancellationToken).ConfigureAwait(false);
        });
        command.Subcommands.Add(open);

        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "open", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteOpenAsync(command.Arguments[1], context, cancellationToken);
        }

        return ExecuteListAsync(context, cancellationToken);
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var statuses = await BuildStatusesAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"{statuses.Count} connection target(s).",
            JsonSerializer.Serialize(statuses, ProtocolJsonContext.Default.ListConnectTargetStatus));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteOpenAsync(string target, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var statuses = await BuildStatusesAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var status = statuses.FirstOrDefault(item => string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase));
        if (status is null || string.IsNullOrWhiteSpace(status.ConnectUrl))
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(false, 1, context.OutputFormat, $"No browser-connectable target '{target}' was found.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }

        OpenBrowser(status.ConnectUrl!);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Opened {status.ConnectUrl}.", null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<List<ConnectTargetStatus>> BuildStatusesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var workspaceRoot = pathService.GetFullPath(workspacePath);
        var config = await sharpClawConfigService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var results = new List<ConnectTargetStatus>();

        foreach (var provider in modelProviders.OrderBy(static provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            var auth = await authFlowService.GetStatusAsync(provider.ProviderName, cancellationToken).ConfigureAwait(false);
            var configuredUrl = config.Document.ConnectLinks?.FirstOrDefault(link => string.Equals(link.Target, provider.ProviderName, StringComparison.OrdinalIgnoreCase))?.Url;
            results.Add(new ConnectTargetStatus(provider.ProviderName, provider.ProviderName, "provider", auth.IsAuthenticated, configuredUrl));
        }

        foreach (var link in config.Document.ConnectLinks ?? [])
        {
            if (results.Any(existing => string.Equals(existing.Target, link.Target, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(new ConnectTargetStatus(link.Target, link.DisplayName, "external", false, link.Url));
        }

        return results.OrderBy(static item => item.Target, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void OpenBrowser(string url)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true }
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", url)
                : new ProcessStartInfo("xdg-open", url);
        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
    }
}
