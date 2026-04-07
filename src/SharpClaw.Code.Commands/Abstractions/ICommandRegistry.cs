namespace SharpClaw.Code.Commands;

/// <summary>
/// Provides discovery for top-level and slash commands.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Gets the registered top-level command handlers.
    /// </summary>
    IReadOnlyList<ICommandHandler> GetCommandHandlers();

    /// <summary>
    /// Gets the registered slash command handlers.
    /// </summary>
    IReadOnlyList<ISlashCommandHandler> GetSlashCommandHandlers();

    /// <summary>
    /// Finds a slash command handler by name.
    /// </summary>
    /// <param name="commandName">The slash command name.</param>
    /// <returns>The matching handler, if found.</returns>
    ISlashCommandHandler? FindSlashCommandHandler(string commandName);
}
