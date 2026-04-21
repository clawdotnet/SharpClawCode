using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Stores durable structured memory entries at project and user scope.
/// </summary>
public interface IPersistentMemoryStore
{
    /// <summary>
    /// Saves or updates a memory entry.
    /// </summary>
    Task<MemoryEntry> SaveAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a memory entry.
    /// </summary>
    Task<bool> DeleteAsync(
        string? workspaceRoot,
        MemoryScope scope,
        string id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists memory entries with optional filtering.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> ListAsync(
        string? workspaceRoot,
        MemoryScope? scope,
        string? query,
        int limit,
        CancellationToken cancellationToken);
}
