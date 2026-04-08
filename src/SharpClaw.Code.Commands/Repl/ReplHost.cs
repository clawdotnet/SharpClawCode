using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Hosts the initial interactive SharpClaw REPL loop.
/// </summary>
public sealed class ReplHost(
    ICommandRegistry commandRegistry,
    IRuntimeCommandService runtimeCommandService,
    ICustomCommandDiscoveryService customCommandDiscovery,
    ReplInteractionState replInteractionState,
    SlashCommandParser slashCommandParser,
    OutputRendererDispatcher outputRendererDispatcher,
    IReplTerminal terminal) : IReplHost
{
    /// <inheritdoc />
    public async Task<int> RunAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var ctrlCPressed = false;

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            ctrlCPressed = true;
            terminal.WriteInfo("Cancellation requested. Type /exit or press Enter to leave the REPL.");
        }

        terminal.CancelKeyPress += OnCancelKeyPress;

        try
        {
            terminal.WriteInfo("SharpClaw Code interactive mode. Type /help for commands or /exit to quit.");

            while (!cancellationToken.IsCancellationRequested && !ctrlCPressed)
            {
                var input = await terminal.ReadLineAsync("sharpclaw> ", cancellationToken);
                if (ctrlCPressed || input is null)
                {
                    break;
                }

                var trimmed = input.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (IsExitCommand(trimmed))
                {
                    break;
                }

                var parsed = slashCommandParser.Parse(trimmed);
                if (parsed.IsSlashCommand)
                {
                    if (IsExitCommand(parsed.CommandName))
                    {
                        break;
                    }

                    if (string.Equals(parsed.CommandName, "help", StringComparison.OrdinalIgnoreCase))
                    {
                        var builtIns = commandRegistry
                            .GetSlashCommandHandlers()
                            .Select(handler => $"/{handler.CommandName} - {handler.Description}")
                            .ToList();
                        var snapshot = await customCommandDiscovery.DiscoverAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
                        var customs = snapshot.Commands.Select(c => $"/{c.Name} - {c.Description ?? "Custom command"}").ToList();
                        var message = string.Join(
                            Environment.NewLine,
                            builtIns
                                .Prepend("/help - Lists available REPL slash commands.")
                                .Concat(customs)
                                .Append("/exit - Leaves interactive mode."));

                        await outputRendererDispatcher.RenderCommandResultAsync(
                            new CommandResult(
                                Succeeded: true,
                                ExitCode: 0,
                                OutputFormat: context.OutputFormat,
                                Message: message,
                                DataJson: null),
                            context.OutputFormat,
                            cancellationToken);
                        continue;
                    }

                    var slashHandler = commandRegistry.FindSlashCommandHandler(parsed.CommandName ?? string.Empty);
                    if (slashHandler is not null)
                    {
                        var exitCode = await slashHandler.ExecuteAsync(parsed, context, cancellationToken);
                        if (exitCode != 0)
                        {
                            return exitCode;
                        }

                        continue;
                    }

                    var customDef = await FindCustomCommandAsync(parsed.CommandName, context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
                    if (customDef is not null)
                    {
                        try
                        {
                            var argLine = string.Join(' ', parsed.Arguments);
                            var result = await runtimeCommandService
                                .ExecuteCustomCommandAsync(customDef.Name, argLine, ToRuntimeContext(context), cancellationToken)
                                .ConfigureAwait(false);
                            await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken);
                        }
                        catch (ProviderExecutionException exception)
                        {
                            await outputRendererDispatcher.RenderCommandResultAsync(
                                CreateProviderFailureResult(exception, context.OutputFormat),
                                context.OutputFormat,
                                cancellationToken);
                        }

                        continue;
                    }

                    await outputRendererDispatcher.RenderCommandResultAsync(
                        new CommandResult(
                            Succeeded: false,
                            ExitCode: 1,
                            OutputFormat: context.OutputFormat,
                            Message: $"Unknown slash command '/{parsed.CommandName}'. Type /help for available commands.",
                            DataJson: null),
                        context.OutputFormat,
                        cancellationToken);
                    continue;
                }

                try
                {
                    var result = await runtimeCommandService.ExecutePromptAsync(
                        trimmed,
                        ToRuntimeContext(context),
                        cancellationToken);

                    await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken);
                }
                catch (ProviderExecutionException exception)
                {
                    await outputRendererDispatcher.RenderCommandResultAsync(
                        CreateProviderFailureResult(exception, context.OutputFormat),
                        context.OutputFormat,
                        cancellationToken);
                }
            }

            terminal.WriteInfo("Leaving interactive mode.");
            return 0;
        }
        finally
        {
            terminal.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private async Task<CustomCommandDefinition?> FindCustomCommandAsync(
        string? name,
        string workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await customCommandDiscovery.FindAsync(workspace, name, cancellationToken).ConfigureAwait(false);
    }

    private RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            replInteractionState.PrimaryModeOverride ?? context.PrimaryMode,
            context.SessionId);

    private static CommandResult CreateProviderFailureResult(ProviderExecutionException exception, OutputFormat outputFormat)
        => new(
            Succeeded: false,
            ExitCode: 1,
            OutputFormat: outputFormat,
            Message: $"Provider failure ({exception.Kind}): {exception.Message}",
            DataJson: null);

    private static bool IsExitCommand(string? value)
        => string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/quit", StringComparison.OrdinalIgnoreCase);
}
