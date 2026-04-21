using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Persists workspace knowledge, search data, and structured memory state.
/// </summary>
public interface IWorkspaceKnowledgeStore
{
    /// <summary>
    /// Replaces the persisted workspace index snapshot.
    /// </summary>
    Task ReplaceWorkspaceIndexAsync(
        string workspaceRoot,
        WorkspaceIndexDocument document,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current workspace index status.
    /// </summary>
    Task<WorkspaceIndexStatus> GetWorkspaceIndexStatusAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Searches indexed chunks lexically through the persisted FTS catalog.
    /// </summary>
    Task<IReadOnlyList<WorkspaceSearchHit>> SearchChunksLexicalAsync(
        string workspaceRoot,
        string query,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Searches indexed symbols by name and container.
    /// </summary>
    Task<IReadOnlyList<WorkspaceSearchHit>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all indexed chunks for semantic ranking.
    /// </summary>
    Task<IReadOnlyList<IndexedWorkspaceChunk>> ListChunksAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Saves or updates a structured memory entry.
    /// </summary>
    Task<MemoryEntry> SaveMemoryAsync(
        string? workspaceRoot,
        MemoryEntry entry,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a structured memory entry.
    /// </summary>
    Task<bool> DeleteMemoryAsync(
        string? workspaceRoot,
        MemoryScope scope,
        string id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists memory entries with optional filtering.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> ListMemoryAsync(
        string? workspaceRoot,
        MemoryScope? scope,
        string? query,
        int limit,
        CancellationToken cancellationToken);
}
