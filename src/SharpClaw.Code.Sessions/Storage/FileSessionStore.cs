using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Stores session snapshots as readable JSON files under the workspace.
/// </summary>
public sealed class FileSessionStore(IFileSystem fileSystem, IRuntimeStoragePathResolver storagePathResolver) : ISessionStore
{
    /// <inheritdoc />
    public Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
    {
        var sessionsRoot = storagePathResolver.GetSessionsRoot(workspacePath);
        fileSystem.CreateDirectory(sessionsRoot);

        var path = storagePathResolver.GetSessionSnapshotPath(workspacePath, session.Id);
        var json = JsonSerializer.Serialize(session, ProtocolJsonContext.Default.ConversationSession);
        return fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetSessionSnapshotPath(workspacePath, sessionId);
        var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content)
            ? null
            : JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.ConversationSession);
    }

    /// <inheritdoc />
    public async Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var sessionsRoot = storagePathResolver.GetSessionsRoot(workspacePath);
        if (!fileSystem.DirectoryExists(sessionsRoot))
        {
            return null;
        }

        ConversationSession? latest = null;
        foreach (var sessionDirectory in fileSystem.EnumerateDirectories(sessionsRoot))
        {
            var sessionId = Path.GetFileName(sessionDirectory);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            var session = await GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                continue;
            }

            latest = latest is null || session.UpdatedAtUtc > latest.UpdatedAtUtc
                ? session
                : latest;
        }

        return latest;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var sessionsRoot = storagePathResolver.GetSessionsRoot(workspacePath);
        if (!fileSystem.DirectoryExists(sessionsRoot))
        {
            return [];
        }

        var list = new List<ConversationSession>();
        foreach (var sessionDirectory in fileSystem.EnumerateDirectories(sessionsRoot))
        {
            var sessionId = Path.GetFileName(sessionDirectory);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                continue;
            }

            var session = await GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
            if (session is not null)
            {
                list.Add(session);
            }
        }

        return list.OrderByDescending(s => s.UpdatedAtUtc).ToArray();
    }
}
