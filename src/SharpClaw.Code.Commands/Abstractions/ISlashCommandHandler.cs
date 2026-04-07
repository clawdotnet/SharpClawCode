using SharpClaw.Code.Commands.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Handles a slash command issued from the REPL shell.
/// </summary>
public interface ISlashCommandHandler
{
    /// <summary>
    /// Gets the slash command name.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Gets the slash command description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the slash command.
    /// </summary>
    /// <param name="command">The parsed slash command.</param>
    /// <param name="context">The current command execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process exit code.</returns>
    Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken);
}
