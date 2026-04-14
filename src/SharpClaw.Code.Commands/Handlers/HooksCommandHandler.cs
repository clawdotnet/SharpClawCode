using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists and tests configured runtime hooks.
/// </summary>
public sealed class HooksCommandHandler(
    IHookDispatcher hookDispatcher,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "hooks";

    /// <inheritdoc />
    public string Description => "Lists, inspects, and tests configured runtime hooks.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildShowCommand(globalOptions));
        command.Subcommands.Add(BuildTestCommand(globalOptions));
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
        => command.Arguments.Length switch
        {
            0 => ExecuteListAsync(context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "show", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 2
                => ExecuteShowAsync(command.Arguments[1], context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "test", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 2
                => ExecuteTestAsync(command.Arguments[1], context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase)
                => ExecuteListAsync(context, cancellationToken),
            _ => ExecuteListAsync(context, cancellationToken)
        };

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists configured hooks.");
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildShowCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("show", "Shows one configured hook.");
        var nameOption = new Option<string>("--name") { Required = true, Description = "Hook name." };
        command.Options.Add(nameOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteShowAsync(parseResult.GetValue(nameOption)!, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildTestCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("test", "Executes one configured hook with a synthetic payload.");
        var nameOption = new Option<string>("--name") { Required = true, Description = "Hook name." };
        command.Options.Add(nameOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteTestAsync(parseResult.GetValue(nameOption)!, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var hooks = await hookDispatcher.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            hooks.Count == 0 ? "No hooks configured." : $"{hooks.Count} hook(s).",
            JsonSerializer.Serialize(hooks.ToList(), ProtocolJsonContext.Default.ListHookStatusRecord));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteShowAsync(string name, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var hooks = await hookDispatcher.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var hook = hooks.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hook is null)
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(false, 1, context.OutputFormat, $"Hook '{name}' was not found.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }

        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{hook.Name} ({hook.Trigger})", JsonSerializer.Serialize(hook, ProtocolJsonContext.Default.HookStatusRecord)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteTestAsync(string name, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["kind"] = "hook-test",
                ["workspaceRoot"] = context.WorkingDirectory,
                ["requestedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
            ProtocolJsonContext.Default.DictionaryStringString);
        var test = await hookDispatcher.TestAsync(context.WorkingDirectory, name, payloadJson, cancellationToken).ConfigureAwait(false);
        var exitCode = test.Succeeded ? 0 : 1;
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(test.Succeeded, exitCode, context.OutputFormat, test.Message, JsonSerializer.Serialize(test, ProtocolJsonContext.Default.HookTestResult)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return exitCode;
    }
}
