using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Persists <c>.sharpclaw/active-session.json</c> with an attached session id.
/// </summary>
public sealed class FileWorkspaceSessionAttachmentStore(IFileSystem fileSystem, IRuntimeStoragePathResolver storagePathResolver)
    : IWorkspaceSessionAttachmentStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc />
    public async Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var path = storagePathResolver.GetWorkspaceActiveSessionPath(workspacePath);
        var json = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ActiveSessionDto>(json, Options);
            return string.IsNullOrWhiteSpace(dto?.SessionId) ? null : dto.SessionId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task SetAttachedSessionIdAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
    {
        var root = storagePathResolver.GetSharpClawRoot(workspacePath);
        fileSystem.CreateDirectory(root);
        var path = storagePathResolver.GetWorkspaceActiveSessionPath(workspacePath);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            fileSystem.TryDeleteFile(path);
            return Task.CompletedTask;
        }

        var payload = JsonSerializer.Serialize(new ActiveSessionDto(sessionId.Trim()), Options);
        return fileSystem.WriteAllTextAsync(path, payload, cancellationToken);
    }

    private sealed record ActiveSessionDto(string SessionId);
}
