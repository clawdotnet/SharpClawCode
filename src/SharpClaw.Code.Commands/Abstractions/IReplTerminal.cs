namespace SharpClaw.Code.Commands;

/// <summary>
/// Represents terminal input and lightweight REPL notifications.
/// </summary>
public interface IReplTerminal
{
    /// <summary>
    /// Raised when Ctrl+C is pressed in the terminal.
    /// </summary>
    event ConsoleCancelEventHandler? CancelKeyPress;

    /// <summary>
    /// Reads a line of user input.
    /// </summary>
    /// <param name="prompt">The prompt to display.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The input line, or <see langword="null"/> if input ended.</returns>
    ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Writes an informational line.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteInfo(string message);

    /// <summary>
    /// Writes an error line.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteError(string message);
}
