using System.Collections.Concurrent;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Bridge;

/// <inheritdoc />
public sealed class EditorContextBuffer(IPathService pathService) : IEditorContextBuffer
{
    private readonly ConcurrentDictionary<string, EditorContextPayload> latest = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Publish(EditorContextPayload payload)
    {
        var key = pathService.GetFullPath(payload.WorkspaceRoot);
        latest[key] = payload;
    }

    /// <inheritdoc />
    public EditorContextPayload? Peek(string normalizedWorkspaceRoot)
    {
        var key = pathService.GetFullPath(normalizedWorkspaceRoot);
        return latest.TryGetValue(key, out var p) ? p : null;
    }

    /// <inheritdoc />
    public EditorContextPayload? TryConsume(string normalizedWorkspaceRoot)
    {
        var key = pathService.GetFullPath(normalizedWorkspaceRoot);
        if (!latest.TryRemove(key, out var payload))
        {
            return null;
        }

        return payload;
    }
}
