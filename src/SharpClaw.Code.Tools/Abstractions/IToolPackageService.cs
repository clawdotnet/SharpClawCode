using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Tools.Abstractions;

/// <summary>
/// Manages packaged third-party tool manifests installed for a workspace.
/// </summary>
public interface IToolPackageService
{
    /// <summary>
    /// Lists installed tool packages for the workspace.
    /// </summary>
    Task<IReadOnlyList<InstalledToolPackage>> ListInstalledAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Installs a tool package manifest and optionally enables its plugin-backed tool surface.
    /// </summary>
    Task<InstalledToolPackage> InstallAsync(
        string workspaceRoot,
        ToolPackageInstallRequest request,
        CancellationToken cancellationToken);
}
