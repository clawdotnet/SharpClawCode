using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>Discovers a contained .NET target without executing project code or restoring packages.</summary>
public interface IDotNetWorkspaceTargetResolver
{
    /// <summary>Resolves an explicit target or reports ambiguous/unsupported workspaces.</summary>
    Task<DotNetWorkspaceTarget> ResolveAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken);
}
