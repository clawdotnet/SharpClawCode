using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Persists durable mutation sets for checkpoint-backed undo/redo.
/// </summary>
public interface IMutationSetStore
{
    /// <summary>
    /// Saves a mutation set document under the session directory.
    /// </summary>
    Task SaveAsync(string workspacePath, MutationSetDocument document, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a mutation set by id, if present.
    /// </summary>
    Task<MutationSetDocument?> GetAsync(string workspacePath, string sessionId, string mutationSetId, CancellationToken cancellationToken);
}
