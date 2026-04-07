using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Holds the latest editor-provided context keyed by normalized workspace root for prompt enrichment.
/// </summary>
public interface IEditorContextBuffer
{
    /// <summary>
    /// Publishes editor context for a workspace; overwrites prior payloads.
    /// </summary>
    void Publish(EditorContextPayload payload);

    /// <summary>
    /// Peeks the latest payload without consuming it.
    /// </summary>
    EditorContextPayload? Peek(string normalizedWorkspaceRoot);

    /// <summary>
    /// Consumes and returns the latest payload for a workspace, if any.
    /// </summary>
    EditorContextPayload? TryConsume(string normalizedWorkspaceRoot);
}
