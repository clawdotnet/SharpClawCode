using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Creates portable, offline session bundles (zip) for sharing without hosted infrastructure.
/// </summary>
public interface IPortableSessionBundleService
{
    /// <summary>
    /// Writes a compressed bundle next to exports (or at <paramref name="outputZipPath"/>).
    /// </summary>
    Task<string> CreateBundleZipAsync(
        string workspacePath,
        string? sessionId,
        string? outputZipPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extracts a bundle zip into <c>.sharpclaw/sessions/{id}/</c> for the workspace.
    /// </summary>
    /// <param name="workspacePath">Target workspace root.</param>
    /// <param name="bundleZipPath">Path to the <c>.sharpclaw-bundle.zip</c> file.</param>
    /// <param name="replaceExisting">When true, replaces an existing session directory with the same id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import metadata.</returns>
    Task<PortableBundleImportResult> ImportBundleZipAsync(
        string workspacePath,
        string bundleZipPath,
        bool replaceExisting,
        CancellationToken cancellationToken);
}
