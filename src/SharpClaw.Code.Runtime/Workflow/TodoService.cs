using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Persists session and workspace todo items using existing SharpClaw durable stores.
/// </summary>
public sealed class TodoService(
    ISessionStore sessionStore,
    IEventStore eventStore,
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    ISystemClock systemClock) : ITodoService
{
    /// <inheritdoc />
    public async Task<TodoSnapshot> GetSnapshotAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var sessionTodos = string.IsNullOrWhiteSpace(sessionId)
            ? []
            : await ReadSessionTodosAsync(normalizedWorkspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        var workspaceTodos = await ReadWorkspaceTodosAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false);

        return new TodoSnapshot(
            normalizedWorkspaceRoot,
            sessionId,
            Sort(sessionTodos),
            Sort(workspaceTodos));
    }

    /// <inheritdoc />
    public async Task<TodoItem> AddAsync(
        string workspaceRoot,
        TodoScope scope,
        string title,
        string? sessionId,
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return scope switch
        {
            TodoScope.Session => await AddSessionTodoAsync(workspaceRoot, sessionId, title, ownerAgentId, cancellationToken).ConfigureAwait(false),
            TodoScope.Workspace => await AddWorkspaceTodoAsync(workspaceRoot, title, ownerAgentId, sessionId, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported todo scope '{scope}'.")
        };
    }

    /// <inheritdoc />
    public async Task<TodoItem> UpdateAsync(
        string workspaceRoot,
        TodoScope scope,
        string todoId,
        string? sessionId,
        string? title,
        TodoStatus? status,
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoId);

        return scope switch
        {
            TodoScope.Session => await UpdateSessionTodoAsync(workspaceRoot, todoId, sessionId, title, status, ownerAgentId, cancellationToken).ConfigureAwait(false),
            TodoScope.Workspace => await UpdateWorkspaceTodoAsync(workspaceRoot, todoId, sessionId, title, status, ownerAgentId, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported todo scope '{scope}'.")
        };
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string workspaceRoot, TodoScope scope, string todoId, string? sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoId);

        return scope switch
        {
            TodoScope.Session => await RemoveSessionTodoAsync(workspaceRoot, todoId, sessionId, cancellationToken).ConfigureAwait(false),
            TodoScope.Workspace => await RemoveWorkspaceTodoAsync(workspaceRoot, todoId, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported todo scope '{scope}'.")
        };
    }

    /// <inheritdoc />
    public async Task<ManagedTodoSyncResult> SyncManagedSessionTodosAsync(
        string workspaceRoot,
        string sessionId,
        string ownerAgentId,
        IReadOnlyList<ManagedTodoSeed> desiredTodos,
        CancellationToken cancellationToken,
        bool assumeSessionLockHeld = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAgentId);
        ArgumentNullException.ThrowIfNull(desiredTodos);

        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var normalizedOwnerAgentId = ownerAgentId.Trim();
        var mapMetadataKey = SharpClawWorkflowMetadataKeys.GetManagedSessionTodoMapKey(normalizedOwnerAgentId);

        if (assumeSessionLockHeld)
        {
            return await SyncManagedSessionTodosCoreAsync(
                normalizedWorkspaceRoot,
                sessionId,
                normalizedOwnerAgentId,
                mapMetadataKey,
                desiredTodos,
                cancellationToken).ConfigureAwait(false);
        }

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetSessionTurnLockPath(normalizedWorkspaceRoot, sessionId), cancellationToken)
            .ConfigureAwait(false);
        return await SyncManagedSessionTodosCoreAsync(
            normalizedWorkspaceRoot,
            sessionId,
            normalizedOwnerAgentId,
            mapMetadataKey,
            desiredTodos,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TodoItem[]> ReadSessionTodosAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
    {
        var resolvedSessionId = RequireSessionId(sessionId);
        var session = await sessionStore.GetByIdAsync(workspaceRoot, resolvedSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{resolvedSessionId}' was not found.");

        if (session.Metadata is null
            || !session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.SessionTodosJson, out var todosJson)
            || string.IsNullOrWhiteSpace(todosJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize(todosJson, ProtocolJsonContext.Default.ListTodoItem)?.ToArray() ?? [];
    }

    private async Task<TodoItem[]> ReadWorkspaceTodosAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetWorkspaceTodosPath(workspaceRoot);
        var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.ListTodoItem)?.ToArray() ?? [];
    }

    private async Task<TodoItem> AddSessionTodoAsync(
        string workspaceRoot,
        string? sessionId,
        string title,
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var resolvedSessionId = RequireSessionId(sessionId);
        var now = systemClock.UtcNow;
        var todo = new TodoItem(
            $"todo-{Guid.NewGuid():N}",
            title.Trim(),
            TodoStatus.Open,
            TodoScope.Session,
            now,
            now,
            Normalize(ownerAgentId),
            resolvedSessionId);

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetSessionTurnLockPath(normalizedWorkspaceRoot, resolvedSessionId), cancellationToken)
            .ConfigureAwait(false);

        var session = await sessionStore.GetByIdAsync(normalizedWorkspaceRoot, resolvedSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{resolvedSessionId}' was not found.");
        var todos = ReadSessionTodoList(session);
        todos.Add(todo);
        await SaveSessionTodosAsync(normalizedWorkspaceRoot, session, todos, "added", todo, cancellationToken).ConfigureAwait(false);
        return todo;
    }

    private async Task<TodoItem> UpdateSessionTodoAsync(
        string workspaceRoot,
        string todoId,
        string? sessionId,
        string? title,
        TodoStatus? status,
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var resolvedSessionId = RequireSessionId(sessionId);

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetSessionTurnLockPath(normalizedWorkspaceRoot, resolvedSessionId), cancellationToken)
            .ConfigureAwait(false);

        var session = await sessionStore.GetByIdAsync(normalizedWorkspaceRoot, resolvedSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{resolvedSessionId}' was not found.");
        var todos = ReadSessionTodoList(session);
        var index = todos.FindIndex(item => string.Equals(item.Id, todoId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException($"Todo '{todoId}' was not found.");
        }

        var existing = todos[index];
        var updated = existing with
        {
            Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title.Trim(),
            Status = status ?? existing.Status,
            OwnerAgentId = Normalize(ownerAgentId) ?? existing.OwnerAgentId,
            UpdatedAtUtc = systemClock.UtcNow,
        };

        todos[index] = updated;
        await SaveSessionTodosAsync(normalizedWorkspaceRoot, session, todos, "updated", updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<bool> RemoveSessionTodoAsync(
        string workspaceRoot,
        string todoId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var resolvedSessionId = RequireSessionId(sessionId);

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetSessionTurnLockPath(normalizedWorkspaceRoot, resolvedSessionId), cancellationToken)
            .ConfigureAwait(false);

        var session = await sessionStore.GetByIdAsync(normalizedWorkspaceRoot, resolvedSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{resolvedSessionId}' was not found.");
        var todos = ReadSessionTodoList(session);
        var removed = todos.FirstOrDefault(item => string.Equals(item.Id, todoId, StringComparison.Ordinal));
        if (removed is null)
        {
            return false;
        }

        todos.RemoveAll(item => string.Equals(item.Id, todoId, StringComparison.Ordinal));
        await SaveSessionTodosAsync(normalizedWorkspaceRoot, session, todos, "removed", removed, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task SaveSessionTodosAsync(
        string workspaceRoot,
        ConversationSession session,
        List<TodoItem> todos,
        string action,
        TodoItem todo,
        CancellationToken cancellationToken)
    {
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata[SharpClawWorkflowMetadataKeys.SessionTodosJson] = JsonSerializer.Serialize(Sort(todos).ToList(), ProtocolJsonContext.Default.ListTodoItem);
        var updatedSession = session with
        {
            UpdatedAtUtc = systemClock.UtcNow,
            Metadata = metadata,
        };

        await sessionStore.SaveAsync(workspaceRoot, updatedSession, cancellationToken).ConfigureAwait(false);
        await eventStore.AppendAsync(
            workspaceRoot,
            updatedSession.Id,
            new TodoChangedEvent(
                $"event-{Guid.NewGuid():N}",
                updatedSession.Id,
                null,
                systemClock.UtcNow,
                action,
                todo),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TodoItem> AddWorkspaceTodoAsync(
        string workspaceRoot,
        string title,
        string? ownerAgentId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        fileSystem.CreateDirectory(storagePathResolver.GetSharpClawRoot(normalizedWorkspaceRoot));
        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetWorkspaceTodosLockPath(normalizedWorkspaceRoot), cancellationToken)
            .ConfigureAwait(false);

        var todos = (await ReadWorkspaceTodosAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false)).ToList();
        var now = systemClock.UtcNow;
        var todo = new TodoItem(
            $"todo-{Guid.NewGuid():N}",
            title.Trim(),
            TodoStatus.Open,
            TodoScope.Workspace,
            now,
            now,
            Normalize(ownerAgentId),
            Normalize(sessionId));
        todos.Add(todo);
        await WriteWorkspaceTodosAsync(normalizedWorkspaceRoot, todos, cancellationToken).ConfigureAwait(false);
        return todo;
    }

    private async Task<TodoItem> UpdateWorkspaceTodoAsync(
        string workspaceRoot,
        string todoId,
        string? sessionId,
        string? title,
        TodoStatus? status,
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        fileSystem.CreateDirectory(storagePathResolver.GetSharpClawRoot(normalizedWorkspaceRoot));
        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetWorkspaceTodosLockPath(normalizedWorkspaceRoot), cancellationToken)
            .ConfigureAwait(false);

        var todos = (await ReadWorkspaceTodosAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false)).ToList();
        var index = todos.FindIndex(item => string.Equals(item.Id, todoId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException($"Todo '{todoId}' was not found.");
        }

        var existing = todos[index];
        var updated = existing with
        {
            Title = string.IsNullOrWhiteSpace(title) ? existing.Title : title.Trim(),
            Status = status ?? existing.Status,
            OwnerAgentId = Normalize(ownerAgentId) ?? existing.OwnerAgentId,
            LinkedSessionId = Normalize(sessionId) ?? existing.LinkedSessionId,
            UpdatedAtUtc = systemClock.UtcNow,
        };

        todos[index] = updated;
        await WriteWorkspaceTodosAsync(normalizedWorkspaceRoot, todos, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<bool> RemoveWorkspaceTodoAsync(string workspaceRoot, string todoId, CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        fileSystem.CreateDirectory(storagePathResolver.GetSharpClawRoot(normalizedWorkspaceRoot));
        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(storagePathResolver.GetWorkspaceTodosLockPath(normalizedWorkspaceRoot), cancellationToken)
            .ConfigureAwait(false);

        var todos = (await ReadWorkspaceTodosAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false)).ToList();
        var removed = todos.RemoveAll(item => string.Equals(item.Id, todoId, StringComparison.Ordinal));
        if (removed == 0)
        {
            return false;
        }

        await WriteWorkspaceTodosAsync(normalizedWorkspaceRoot, todos, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task WriteWorkspaceTodosAsync(string workspaceRoot, IReadOnlyList<TodoItem> todos, CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(
            storagePathResolver.GetWorkspaceTodosPath(workspaceRoot),
            JsonSerializer.Serialize(Sort(todos).ToList(), ProtocolJsonContext.Default.ListTodoItem),
            cancellationToken);

    private static Dictionary<string, string> ReadManagedTodoMap(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.DictionaryStringString)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static TodoItem? ResolveManagedTodo(
        IReadOnlyDictionary<string, string> existingMap,
        IReadOnlyList<TodoItem> todos,
        string ownerAgentId,
        string externalId,
        Queue<TodoItem> unusedManagedTodos)
    {
        if (existingMap.TryGetValue(externalId, out var todoId))
        {
            var mapped = todos.FirstOrDefault(item =>
                string.Equals(item.Id, todoId, StringComparison.Ordinal)
                && string.Equals(item.OwnerAgentId, ownerAgentId, StringComparison.Ordinal));
            if (mapped is not null)
            {
                return mapped;
            }
        }

        return unusedManagedTodos.Count > 0 ? unusedManagedTodos.Dequeue() : null;
    }

    private static List<TodoItem> ReadSessionTodoList(ConversationSession session)
        => session.Metadata is not null
           && session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.SessionTodosJson, out var todosJson)
           && !string.IsNullOrWhiteSpace(todosJson)
            ? JsonSerializer.Deserialize(todosJson, ProtocolJsonContext.Default.ListTodoItem)?.ToList() ?? []
            : [];

    private static TodoItem[] Sort(IEnumerable<TodoItem> items)
        => items
            .OrderBy(static item => item.Status == TodoStatus.Done)
            .ThenBy(static item => item.UpdatedAtUtc)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool DictionaryEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count
           && left.All(pair => right.TryGetValue(pair.Key, out var rightValue) && string.Equals(pair.Value, rightValue, StringComparison.Ordinal));

    private static string RequireSessionId(string? sessionId)
        => string.IsNullOrWhiteSpace(sessionId)
            ? throw new InvalidOperationException("A session id is required for session-scoped todos.")
            : sessionId;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ManagedTodoSyncResult> SyncManagedSessionTodosCoreAsync(
        string normalizedWorkspaceRoot,
        string sessionId,
        string normalizedOwnerAgentId,
        string mapMetadataKey,
        IReadOnlyList<ManagedTodoSeed> desiredTodos,
        CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetByIdAsync(normalizedWorkspaceRoot, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var todos = ReadSessionTodoList(session);
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        var existingMap = ReadManagedTodoMap(metadata, mapMetadataKey);
        var normalizedDesired = desiredTodos
            .Where(static item => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(static item => item.ExternalId.Trim(), StringComparer.Ordinal)
            .Select(static group => group.Last() with { ExternalId = group.Key, Title = group.Last().Title.Trim() })
            .ToArray();

        var nextMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var touchedTodoIds = new HashSet<string>(StringComparer.Ordinal);
        var unusedManagedTodos = new Queue<TodoItem>(todos.Where(item =>
            string.Equals(item.OwnerAgentId, normalizedOwnerAgentId, StringComparison.Ordinal)
            && !existingMap.Values.Contains(item.Id, StringComparer.Ordinal)));
        var mutatedTodos = new List<(string Action, TodoItem Todo)>();
        var addedCount = 0;
        var updatedCount = 0;

        foreach (var seed in normalizedDesired)
        {
            var existing = ResolveManagedTodo(existingMap, todos, normalizedOwnerAgentId, seed.ExternalId, unusedManagedTodos);
            if (existing is null)
            {
                var now = systemClock.UtcNow;
                var created = new TodoItem(
                    $"todo-{Guid.NewGuid():N}",
                    seed.Title,
                    seed.Status,
                    TodoScope.Session,
                    now,
                    now,
                    normalizedOwnerAgentId,
                    sessionId);
                todos.Add(created);
                nextMap[seed.ExternalId] = created.Id;
                touchedTodoIds.Add(created.Id);
                mutatedTodos.Add(("added", created));
                addedCount++;
                continue;
            }

            var updated = existing with
            {
                Title = seed.Title,
                Status = seed.Status,
                OwnerAgentId = normalizedOwnerAgentId,
                LinkedSessionId = sessionId,
                UpdatedAtUtc = systemClock.UtcNow,
            };

            var index = todos.FindIndex(item => string.Equals(item.Id, existing.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                continue;
            }

            if (!Equals(existing, updated))
            {
                todos[index] = updated;
                mutatedTodos.Add(("updated", updated));
                updatedCount++;
            }

            nextMap[seed.ExternalId] = updated.Id;
            touchedTodoIds.Add(updated.Id);
        }

        var removedTodos = todos
            .Where(item => string.Equals(item.OwnerAgentId, normalizedOwnerAgentId, StringComparison.Ordinal)
                && !touchedTodoIds.Contains(item.Id))
            .ToArray();
        foreach (var removed in removedTodos)
        {
            todos.RemoveAll(item => string.Equals(item.Id, removed.Id, StringComparison.Ordinal));
            mutatedTodos.Add(("removed", removed));
        }

        var removedCount = removedTodos.Length;
        var metadataChanged = !DictionaryEquals(existingMap, nextMap);
        if (mutatedTodos.Count > 0 || metadataChanged)
        {
            metadata[SharpClawWorkflowMetadataKeys.SessionTodosJson] = JsonSerializer.Serialize(Sort(todos).ToList(), ProtocolJsonContext.Default.ListTodoItem);
            if (nextMap.Count == 0)
            {
                metadata.Remove(mapMetadataKey);
            }
            else
            {
                metadata[mapMetadataKey] = JsonSerializer.Serialize(nextMap, ProtocolJsonContext.Default.DictionaryStringString);
            }

            var updatedSession = session with
            {
                UpdatedAtUtc = systemClock.UtcNow,
                Metadata = metadata,
            };

            await sessionStore.SaveAsync(normalizedWorkspaceRoot, updatedSession, cancellationToken).ConfigureAwait(false);
            foreach (var mutation in mutatedTodos)
            {
                await eventStore.AppendAsync(
                    normalizedWorkspaceRoot,
                    updatedSession.Id,
                    new TodoChangedEvent(
                        $"event-{Guid.NewGuid():N}",
                        updatedSession.Id,
                        null,
                        systemClock.UtcNow,
                        mutation.Action,
                        mutation.Todo),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new ManagedTodoSyncResult(
            normalizedOwnerAgentId,
            addedCount,
            updatedCount,
            removedCount,
            Sort(todos.Where(item => string.Equals(item.OwnerAgentId, normalizedOwnerAgentId, StringComparison.Ordinal))));
    }
}
