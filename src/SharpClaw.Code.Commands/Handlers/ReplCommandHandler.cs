using System.CommandLine;
using SharpClaw.Code.Commands.Options;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Builds the explicit REPL command.
/// </summary>
public sealed class ReplCommandHandler(IReplHost replHost)
{
    /// <summary>
    /// Builds the repl command.
    /// </summary>
    /// <param name="globalOptions">The shared global options.</param>
    /// <returns>A configured repl command.</returns>
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("repl", "Starts the interactive REPL shell.");
        command.SetAction((parseResult, cancellationToken) => replHost.RunAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }
}
