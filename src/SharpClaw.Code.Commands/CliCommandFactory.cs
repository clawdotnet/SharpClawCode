using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Creates the root command surface for the SharpClaw CLI.
/// </summary>
public sealed class CliCommandFactory(
    ICommandRegistry commandRegistry,
    GlobalCliOptions globalCliOptions,
    IReplHost replHost,
    ReplCommandHandler replCommandHandler,
    ICustomCommandDiscoveryService customCommandDiscovery,
    IPathService pathService,
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher)
{
    /// <summary>
    /// Creates the root command for the current scaffold.
    /// </summary>
    /// <returns>A configured root command.</returns>
    public async Task<RootCommand> CreateRootCommandAsync(CancellationToken cancellationToken = default)
    {
        var rootCommand = new RootCommand("SharpClaw Code CLI. Starts interactive mode when no command is supplied.");

        foreach (var option in globalCliOptions.All)
        {
            rootCommand.Options.Add(option);
        }

        foreach (var handler in commandRegistry.GetCommandHandlers())
        {
            rootCommand.Subcommands.Add(handler.BuildCommand(globalCliOptions));
        }

        await AddDiscoveredCustomCommandsAsync(rootCommand, cancellationToken).ConfigureAwait(false);

        rootCommand.Subcommands.Add(replCommandHandler.BuildCommand(globalCliOptions));
        rootCommand.SetAction((parseResult, ct) => replHost.RunAsync(globalCliOptions.Resolve(parseResult), ct));
        return rootCommand;
    }

    private async Task AddDiscoveredCustomCommandsAsync(RootCommand rootCommand, CancellationToken cancellationToken)
    {
        var reserved = new HashSet<string>(
            commandRegistry.GetCommandHandlers().Select(static h => h.Name).Append("repl"),
            StringComparer.OrdinalIgnoreCase);

        var workspace = pathService.GetFullPath(pathService.GetCurrentDirectory());
        var snapshot = await customCommandDiscovery.DiscoverAsync(workspace, cancellationToken).ConfigureAwait(false);

        foreach (var definition in snapshot.Commands)
        {
            if (reserved.Contains(definition.Name))
            {
                continue;
            }

            var command = new Command(definition.Name, definition.Description ?? $"Custom SharpClaw command '{definition.Name}'.");
            var args = new Argument<string[]>("args")
            {
                Description = "Arguments passed to the command template.",
                Arity = ArgumentArity.ZeroOrMore,
            };
            command.Arguments.Add(args);
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var ctx = globalCliOptions.Resolve(parseResult);
                var parts = parseResult.GetValue(args) ?? [];
                var argLine = string.Join(' ', parts);
                try
                {
                    var result = await runtimeCommandService
                        .ExecuteCustomCommandAsync(definition.Name, argLine, ToRuntimeContext(ctx), cancellationToken)
                        .ConfigureAwait(false);
                    await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, ctx.OutputFormat, cancellationToken).ConfigureAwait(false);
                    return 0;
                }
                catch (ProviderExecutionException exception)
                {
                    await outputRendererDispatcher.RenderCommandResultAsync(
                        new CommandResult(
                            false,
                            1,
                            ctx.OutputFormat,
                            $"Provider failure ({exception.Kind}): {exception.Message}",
                            null),
                        ctx.OutputFormat,
                        cancellationToken).ConfigureAwait(false);
                    return 1;
                }
            });
            rootCommand.Subcommands.Add(command);
        }
    }

    private static RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.SessionId);
}
