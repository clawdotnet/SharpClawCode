using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// File-backed <see cref="IMutationSetStore"/> under each session's <c>mutations</c> directory.
/// </summary>
public sealed class FileMutationSetStore(IFileSystem fileSystem, IRuntimeStoragePathResolver storagePathResolver) : IMutationSetStore
{
    /// <inheritdoc />
    public Task SaveAsync(string workspacePath, MutationSetDocument document, CancellationToken cancellationToken)
    {
        var dir = storagePathResolver.GetMutationsRoot(workspacePath, document.SessionId);
        fileSystem.CreateDirectory(dir);
        var path = storagePathResolver.GetMutationSetPath(workspacePath, document.SessionId, document.Id);
        var json = JsonSerializer.Serialize(document, ProtocolJsonContext.Default.MutationSetDocument);
        return fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MutationSetDocument?> GetAsync(string workspacePath, string sessionId, string mutationSetId, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetMutationSetPath(workspacePath, sessionId, mutationSetId);
        var text = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : JsonSerializer.Deserialize(text, ProtocolJsonContext.Default.MutationSetDocument);
    }
}
