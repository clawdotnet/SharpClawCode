using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Verifies the workspace directory exists.
/// </summary>
public sealed class WorkspaceAccessibilityCheck(IFileSystem fileSystem, IPathService pathService) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "workspace.access";

    /// <inheritdoc />
    public Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var fullPath = pathService.GetFullPath(context.NormalizedWorkspacePath);
        if (!fileSystem.DirectoryExists(fullPath))
        {
            return Task.FromResult(new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Error,
                "Workspace directory is missing.",
                fullPath));
        }

        return Task.FromResult(new OperationalCheckItem(
            Id,
            OperationalCheckStatus.Ok,
            "Workspace directory is reachable.",
            fullPath));
    }
}
