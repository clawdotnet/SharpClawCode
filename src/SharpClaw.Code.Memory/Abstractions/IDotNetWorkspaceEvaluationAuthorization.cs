using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>Authorizes project-controlled design-time execution. Hosts must implement this through their permission policy.</summary>
public interface IDotNetWorkspaceEvaluationAuthorization
{
    /// <summary>Authorizes a fresh load/reload. Returning false must cause no project execution.</summary>
    Task<bool> AuthorizeAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken);
}
