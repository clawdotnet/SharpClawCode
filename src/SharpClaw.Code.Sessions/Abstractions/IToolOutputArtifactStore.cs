using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>Stores complete tool text in the caller's tenant and session partition.</summary>
public interface IToolOutputArtifactStore
{
    /// <summary>Publishes an immutable artifact before returning its identifier.</summary>
    Task<ToolOutputArtifact> SaveAsync(string workspace, string sessionId, string turnId, string toolName, string content, CancellationToken cancellationToken);
    /// <summary>Reads only from the current session; identifiers cannot select another session or a filesystem path.</summary>
    Task<ToolOutputArtifact?> GetAsync(string workspace, string sessionId, string artifactId, CancellationToken cancellationToken);
}
