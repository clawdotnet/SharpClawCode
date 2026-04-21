using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Hosts the embedded SharpClaw HTTP server for local editor and automation clients.
/// </summary>
public sealed class ServeCommandHandler(
    IWorkspaceHttpServer workspaceHttpServer,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "serve";

    /// <inheritdoc />
    public string Description => "Runs the embedded SharpClaw HTTP server.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        var hostOption = new Option<string?>("--host") { Description = "Optional bind host override." };
        var portOption = new Option<int?>("--port") { Description = "Optional bind port override." };
        command.Options.Add(hostOption);
        command.Options.Add(portOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(true, 0, context.OutputFormat, "Starting embedded SharpClaw server. Press Ctrl+C to stop.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            await workspaceHttpServer
                .RunAsync(context.WorkingDirectory, host, port, context.ToRuntimeCommandContext(), cancellationToken)
                .ConfigureAwait(false);
            return 0;
        });
        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        string? host = null;
        int? port = null;
        if (command.Arguments.Length >= 1)
        {
            host = command.Arguments[0];
        }

        if (command.Arguments.Length >= 2 && int.TryParse(command.Arguments[1], out var parsedPort))
        {
            port = parsedPort;
        }

        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, "Starting embedded SharpClaw server. Press Ctrl+C to stop.", null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        await workspaceHttpServer.RunAsync(context.WorkingDirectory, host, port, context.ToRuntimeCommandContext(), cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
