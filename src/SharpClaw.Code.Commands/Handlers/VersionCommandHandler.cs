using System.CommandLine;
using System.Reflection;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Implements the version command.
/// </summary>
public sealed class VersionCommandHandler(OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "version";

    /// <inheritdoc />
    public string Description => "Displays version information for SharpClaw Code.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            await outputRendererDispatcher.RenderCommandResultAsync(CreateResult(), context.OutputFormat, cancellationToken);
            return 0;
        });

        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(CreateResult(), context.OutputFormat, cancellationToken);
        return 0;
    }

    private static CommandResult CreateResult()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
        var payload = $"{{\"version\":\"{version}\"}}";

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: OutputFormat.Text,
            Message: $"SharpClaw Code version {version}",
            DataJson: payload);
    }
}
