using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Abstractions;

/// <summary>
/// Normalizes external plugin manifest formats into SharpClaw-local install requests.
/// </summary>
public interface IPluginManifestImportService
{
    /// <summary>
    /// Imports a manifest payload using an explicit or auto-detected format.
    /// </summary>
    Task<(PluginInstallRequest Request, ImportedPluginManifestResult Result)> ImportAsync(
        string manifestPath,
        string? format,
        CancellationToken cancellationToken);
}
