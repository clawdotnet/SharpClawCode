using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>Atomically publishes immutable output artifacts using tenant-aware session storage.</summary>
public sealed class FileToolOutputArtifactStore(IFileSystem files, IPathService paths, IRuntimeStoragePathResolver storage) : IToolOutputArtifactStore
{
    /// <inheritdoc />
    public async Task<ToolOutputArtifact> SaveAsync(string workspace, string sessionId, string turnId, string toolName, string content, CancellationToken cancellationToken)
    {
        var artifact = new ToolOutputArtifact("artifact-" + Guid.NewGuid().ToString("N"), sessionId, turnId, toolName, DateTimeOffset.UtcNow, content);
        var path = Resolve(workspace, sessionId, artifact.Id);
        files.CreateDirectory(Path.GetDirectoryName(path)!);
        // IFileSystem.WriteAllTextAsync already writes through a same-directory temporary file and atomic replace.
        await files.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, ProtocolJsonContext.Default.ToolOutputArtifact), cancellationToken).ConfigureAwait(false);
        return artifact;
    }

    /// <inheritdoc />
    public async Task<ToolOutputArtifact?> GetAsync(string workspace, string sessionId, string artifactId, CancellationToken cancellationToken)
    {
        var text = await files.ReadAllTextIfExistsAsync(Resolve(workspace, sessionId, artifactId), cancellationToken).ConfigureAwait(false);
        if (text is null) return null;
        var artifact = JsonSerializer.Deserialize(text, ProtocolJsonContext.Default.ToolOutputArtifact);
        if (artifact is null || artifact.Id != artifactId || artifact.SessionId != sessionId) throw new InvalidDataException("Artifact identity does not match its session.");
        return artifact;
    }

    private string Resolve(string workspace, string sessionId, string artifactId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 128 || sessionId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Invalid session identifier.", nameof(sessionId));
        if (!artifactId.StartsWith("artifact-", StringComparison.Ordinal) || !Guid.TryParseExact(artifactId[9..], "N", out _))
            throw new ArgumentException("Invalid artifact identifier.", nameof(artifactId));
        var root = paths.GetCanonicalFullPath(storage.GetSessionRoot(workspace, sessionId));
        var path = paths.GetCanonicalFullPath(Path.Combine(root, "artifacts", artifactId + ".json"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sessions = paths.GetCanonicalFullPath(storage.GetSessionsRoot(workspace)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(sessions, comparison) || !path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Artifact path escapes its session storage boundary.");
        return path;
    }
}
