using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Stores durable project and user memory entries through the workspace knowledge store.
/// </summary>
public sealed class PersistentMemoryStore(IWorkspaceKnowledgeStore knowledgeStore) : IPersistentMemoryStore
{
    /// <inheritdoc />
    public Task<MemoryEntry> SaveAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken)
        => knowledgeStore.SaveMemoryAsync(workspaceRoot, entry, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        string? workspaceRoot,
        MemoryScope scope,
        string id,
        CancellationToken cancellationToken)
        => knowledgeStore.DeleteMemoryAsync(workspaceRoot, scope, id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string? workspaceRoot,
        MemoryScope? scope,
        string? query,
        int limit,
        CancellationToken cancellationToken)
        => knowledgeStore.ListMemoryAsync(workspaceRoot, scope, query, limit, cancellationToken);
}
