namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Opens a compose buffer in the user-configured external editor and returns edited text.
/// </summary>
public interface IExternalEditorService
{
    /// <summary>
    /// Creates a temp file, launches the editor (blocking until exit), then reads the file.
    /// </summary>
    /// <param name="initialContent">Optional seed content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Edited text, or null if the editor was missing, launch failed, or content was empty (cancelled).</returns>
    Task<string?> ComposeAsync(string? initialContent, CancellationToken cancellationToken);
}
