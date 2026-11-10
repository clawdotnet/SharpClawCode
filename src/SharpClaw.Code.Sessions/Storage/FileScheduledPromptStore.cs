using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores scheduled prompts as a workspace-local JSON catalog.
/// </summary>
public sealed class FileScheduledPromptStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : IScheduledPromptStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledPromptDefinition>> ListAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetScheduledPromptsPath(workspacePath);
        var items = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return items
            .OrderBy(static item => item.NextRunUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<ScheduledPromptDefinition?> GetByIdAsync(string workspacePath, string scheduleId, CancellationToken cancellationToken)
        => (await LoadAsync(storagePathResolver.GetScheduledPromptsPath(workspacePath), cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, scheduleId, StringComparison.Ordinal));

    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, ScheduledPromptDefinition definition, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetScheduledPromptsPath(workspacePath);
        var lockPath = storagePathResolver.GetScheduledPromptsLockPath(workspacePath);
        await using var gate = await fileSystem.AcquireExclusiveFileLockAsync(lockPath, cancellationToken).ConfigureAwait(false);

        var items = (await LoadAsync(path, cancellationToken).ConfigureAwait(false)).ToList();
        var index = items.FindIndex(item => string.Equals(item.Id, definition.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            items[index] = definition;
        }
        else
        {
            items.Add(definition);
        }

        await SaveAsync(path, items, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string workspacePath, string scheduleId, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetScheduledPromptsPath(workspacePath);
        var lockPath = storagePathResolver.GetScheduledPromptsLockPath(workspacePath);
        await using var gate = await fileSystem.AcquireExclusiveFileLockAsync(lockPath, cancellationToken).ConfigureAwait(false);

        var items = (await LoadAsync(path, cancellationToken).ConfigureAwait(false)).ToList();
        var removed = items.RemoveAll(item => string.Equals(item.Id, scheduleId, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            await SaveAsync(path, items, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<IReadOnlyList<ScheduledPromptDefinition>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.ListScheduledPromptDefinition) ?? [];
    }

    private Task SaveAsync(string path, IReadOnlyList<ScheduledPromptDefinition> items, CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(items.ToList(), ProtocolJsonContext.Default.ListScheduledPromptDefinition),
            cancellationToken);
}
