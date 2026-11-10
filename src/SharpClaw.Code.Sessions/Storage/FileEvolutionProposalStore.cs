using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores evolution proposals as a workspace-local JSON catalog.
/// </summary>
public sealed class FileEvolutionProposalStore(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : IEvolutionProposalStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EvolutionProposal>> ListAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetEvolutionProposalsPath(workspacePath);
        var items = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return items
            .OrderByDescending(static item => item.UpdatedAtUtc ?? item.CreatedAtUtc)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<EvolutionProposal?> GetByIdAsync(string workspacePath, string proposalId, CancellationToken cancellationToken)
        => (await LoadAsync(storagePathResolver.GetEvolutionProposalsPath(workspacePath), cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, proposalId, StringComparison.Ordinal));

    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, EvolutionProposal proposal, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetEvolutionProposalsPath(workspacePath);
        var lockPath = storagePathResolver.GetEvolutionProposalsLockPath(workspacePath);
        await using var gate = await fileSystem.AcquireExclusiveFileLockAsync(lockPath, cancellationToken).ConfigureAwait(false);

        var items = (await LoadAsync(path, cancellationToken).ConfigureAwait(false)).ToList();
        var index = items.FindIndex(item => string.Equals(item.Id, proposal.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            items[index] = proposal;
        }
        else
        {
            items.Add(proposal);
        }

        await SaveAsync(path, items, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string workspacePath, string proposalId, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetEvolutionProposalsPath(workspacePath);
        var lockPath = storagePathResolver.GetEvolutionProposalsLockPath(workspacePath);
        await using var gate = await fileSystem.AcquireExclusiveFileLockAsync(lockPath, cancellationToken).ConfigureAwait(false);

        var items = (await LoadAsync(path, cancellationToken).ConfigureAwait(false)).ToList();
        var removed = items.RemoveAll(item => string.Equals(item.Id, proposalId, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            await SaveAsync(path, items, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<IReadOnlyList<EvolutionProposal>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.ListEvolutionProposal) ?? [];
    }

    private Task SaveAsync(string path, IReadOnlyList<EvolutionProposal> items, CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(items.ToList(), ProtocolJsonContext.Default.ListEvolutionProposal),
            cancellationToken);
}
