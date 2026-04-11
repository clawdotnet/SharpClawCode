namespace SharpClaw.Code.Commands;

/// <summary>
/// Stores the registered top-level and slash command handlers.
/// </summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly IReadOnlyList<ICommandHandler> _commandHandlers;
    private readonly IReadOnlyList<ISlashCommandHandler> _slashCommandHandlers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandRegistry"/> class.
    /// </summary>
    public CommandRegistry(
        IEnumerable<ICommandHandler> commandHandlers,
        IEnumerable<ISlashCommandHandler> slashCommandHandlers)
    {
        _commandHandlers = commandHandlers.OrderBy(handler => handler.Name).ToArray();
        _slashCommandHandlers = slashCommandHandlers.OrderBy(handler => handler.CommandName).ToArray();

        var duplicateCommand = _commandHandlers
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateCommand is not null)
        {
            throw new InvalidOperationException($"Duplicate command handler registered: '{duplicateCommand.Key}'.");
        }

        var duplicateSlash = _slashCommandHandlers
            .GroupBy(h => h.CommandName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateSlash is not null)
        {
            throw new InvalidOperationException($"Duplicate slash command handler registered: '{duplicateSlash.Key}'.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ICommandHandler> GetCommandHandlers() => _commandHandlers;

    /// <inheritdoc />
    public IReadOnlyList<ISlashCommandHandler> GetSlashCommandHandlers() => _slashCommandHandlers;

    /// <inheritdoc />
    public ISlashCommandHandler? FindSlashCommandHandler(string commandName)
        => _slashCommandHandlers.FirstOrDefault(handler => string.Equals(handler.CommandName, commandName, StringComparison.OrdinalIgnoreCase));
}
