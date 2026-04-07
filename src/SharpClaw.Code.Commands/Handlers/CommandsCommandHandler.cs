using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists or refreshes discovered markdown commands.
/// </summary>
public sealed class CommandsCommandHandler(
    ICustomCommandDiscoveryService discovery,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "commands";

    /// <inheritdoc />
    public string Description => "Lists custom markdown commands from workspace and user profile.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var cmd = new Command(Name, Description);
        var list = new Command("list", "Lists discovered commands and any issues.");
        list.SetAction(async (parseResult, cancellationToken) =>
        {
            var ctx = globalOptions.Resolve(parseResult);
            var snapshot = await discovery.DiscoverAsync(ctx.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(snapshot, ProtocolJsonContext.Default.CustomCommandCatalogSnapshot);
            var lines = snapshot.Commands.Select(c => $"{c.Name} ({c.SourceScope}) — {c.Description ?? "no description"}").ToArray();
            var message = string.Join(Environment.NewLine, lines.Prepend($"Discovered {snapshot.Commands.Count} command(s)."));
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(true, 0, ctx.OutputFormat, message, payload),
                ctx.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 0;
        });
        var refresh = new Command("refresh", "Redisplays the catalog (same as list for now).");
        refresh.SetAction(async (parseResult, cancellationToken) =>
        {
            var ctx = globalOptions.Resolve(parseResult);
            var snapshot = await discovery.DiscoverAsync(ctx.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            var message = $"Refreshed catalog: {snapshot.Commands.Count} command(s), {snapshot.Issues.Count} issue(s).";
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(true, 0, ctx.OutputFormat, message, null),
                ctx.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 0;
        });
        cmd.Subcommands.Add(list);
        cmd.Subcommands.Add(refresh);
        return cmd;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var sub = command.Arguments.Length > 0 ? command.Arguments[0] : "list";
        if (string.Equals(sub, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            var snap = await discovery.DiscoverAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            var message = $"Refreshed catalog: {snap.Commands.Count} command(s), {snap.Issues.Count} issue(s).";
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(true, 0, context.OutputFormat, message, null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var snapshot = await discovery.DiscoverAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var lines = snapshot.Commands.Select(c => $"{c.Name} ({c.SourceScope}) — {c.Description ?? "no description"}").ToArray();
        var msg = string.Join(Environment.NewLine, lines.Prepend($"Discovered {snapshot.Commands.Count} command(s)."));
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, msg, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
