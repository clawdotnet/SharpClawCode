using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Builds portable exports of session snapshots and persisted event tails.
/// </summary>
public interface ISessionExportService
{
    /// <summary>
    /// Produces export content for a session.
    /// </summary>
    /// <param name="workspacePath">Workspace root.</param>
    /// <param name="sessionId">Session id; null selects latest.</param>
    /// <param name="format">Markdown or JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured document plus suggested file extension (without dot).</returns>
    Task<(SessionExportDocument Document, string SuggestedExtension)> BuildDocumentAsync(
        string workspacePath,
        string? sessionId,
        SessionExportFormat format,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a Markdown document as text.
    /// </summary>
    string RenderMarkdown(SessionExportDocument document);
}
