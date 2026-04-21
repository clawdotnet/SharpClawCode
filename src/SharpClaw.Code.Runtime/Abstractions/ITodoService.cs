using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Manages durable session-scoped and workspace-scoped todo items.
/// </summary>
public interface ITodoService
{
    /// <summary>
    /// Returns the current todo snapshot for the workspace and optional session.
    /// </summary>
    Task<TodoSnapshot> GetSnapshotAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a todo item to the requested scope.
    /// </summary>
    Task<TodoItem> AddAsync(
        string workspaceRoot,
        TodoScope scope,
        string title,
        string? sessionId,
        string? ownerAgentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing todo item.
    /// </summary>
    Task<TodoItem> UpdateAsync(
        string workspaceRoot,
        TodoScope scope,
        string todoId,
        string? sessionId,
        string? title,
        TodoStatus? status,
        string? ownerAgentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an existing todo item.
    /// </summary>
    Task<bool> RemoveAsync(string workspaceRoot, TodoScope scope, string todoId, string? sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Reconciles a planning or agent-owned managed todo set against the current session-scoped todos.
    /// </summary>
    Task<ManagedTodoSyncResult> SyncManagedSessionTodosAsync(
        string workspaceRoot,
        string sessionId,
        string ownerAgentId,
        IReadOnlyList<ManagedTodoSeed> desiredTodos,
        CancellationToken cancellationToken,
        bool assumeSessionLockHeld = false);
}
