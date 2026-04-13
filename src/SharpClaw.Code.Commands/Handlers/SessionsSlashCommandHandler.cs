using SharpClaw.Code.Commands.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Provides a friendlier plural alias over the session slash command.
/// </summary>
public sealed class SessionsSlashCommandHandler(SessionCommandHandler sessionCommandHandler) : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "sessions";

    /// <inheritdoc />
    public string Description => "Alias for /session list and /session show.";

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var translated = command.Arguments.Length == 0
            ? new SlashCommandParseResult(true, "session", ["list"])
            : new SlashCommandParseResult(true, "session", command.Arguments);
        return sessionCommandHandler.ExecuteAsync(translated, context, cancellationToken);
    }
}
