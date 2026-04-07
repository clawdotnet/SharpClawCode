namespace SharpClaw.Code.Commands;

/// <summary>
/// Stores the registered top-level and slash command handlers.
/// </summary>
public sealed class CommandRegistry(
    IEnumerable<ICommandHandler> commandHandlers,
    IEnumerable<ISlashCommandHandler> slashCommandHandlers) : ICommandRegistry
{
    private readonly IReadOnlyList<ICommandHandler> _commandHandlers = commandHandlers.OrderBy(handler => handler.Name).ToArray();
    private readonly IReadOnlyList<ISlashCommandHandler> _slashCommandHandlers = slashCommandHandlers.OrderBy(handler => handler.CommandName).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<ICommandHandler> GetCommandHandlers() => _commandHandlers;

    /// <inheritdoc />
    public IReadOnlyList<ISlashCommandHandler> GetSlashCommandHandlers() => _slashCommandHandlers;

    /// <inheritdoc />
    public ISlashCommandHandler? FindSlashCommandHandler(string commandName)
        => _slashCommandHandlers.FirstOrDefault(handler => string.Equals(handler.CommandName, commandName, StringComparison.OrdinalIgnoreCase));
}
