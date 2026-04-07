using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores runtime checkpoints as readable JSON files under each session.
/// </summary>
public sealed class FileCheckpointStore(IFileSystem fileSystem, IPathService pathService) : ICheckpointStore
{
    /// <inheritdoc />
    public Task SaveAsync(string workspacePath, RuntimeCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(checkpoint, ProtocolJsonContext.Default.RuntimeCheckpoint);
        var path = SessionStorageLayout.GetCheckpointPath(pathService, workspacePath, checkpoint.SessionId, checkpoint.Id);
        return fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RuntimeCheckpoint?> GetLatestAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        var checkpointsRoot = SessionStorageLayout.GetCheckpointsRoot(pathService, workspacePath, sessionId);
        if (!fileSystem.DirectoryExists(checkpointsRoot))
        {
            return null;
        }

        RuntimeCheckpoint? latest = null;
        foreach (var path in fileSystem.EnumerateFiles(checkpointsRoot, "*.json"))
        {
            var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var checkpoint = JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.RuntimeCheckpoint);
            if (checkpoint is null)
            {
                continue;
            }

            latest = latest is null || checkpoint.CreatedAtUtc > latest.CreatedAtUtc
                ? checkpoint
                : latest;
        }

        return latest;
    }
}
