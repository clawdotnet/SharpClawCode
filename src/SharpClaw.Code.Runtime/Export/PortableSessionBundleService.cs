using System.IO.Compression;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;

namespace SharpClaw.Code.Runtime.Export;

/// <inheritdoc />
public sealed class PortableSessionBundleService(
    ISessionStore sessionStore,
    IFileSystem fileSystem,
    IPathService pathService) : IPortableSessionBundleService
{
    /// <inheritdoc />
    public async Task<string> CreateBundleZipAsync(
        string workspacePath,
        string? sessionId,
        string? outputZipPath,
        CancellationToken cancellationToken)
    {
        var workspace = pathService.GetFullPath(workspacePath);
        var session = string.IsNullOrWhiteSpace(sessionId)
            ? await sessionStore.GetLatestAsync(workspace, cancellationToken).ConfigureAwait(false)
            : await sessionStore.GetByIdAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException("No session found to bundle.");
        }

        var sessionRoot = SessionStorageLayout.GetSessionRoot(pathService, workspace, session.Id);
        if (!fileSystem.DirectoryExists(sessionRoot))
        {
            throw new InvalidOperationException($"Session directory for '{session.Id}' was not found.");
        }

        var staging = pathService.Combine(pathService.GetTempPath(), $"sharpclaw-bundle-{session.Id}-{Guid.NewGuid():N}");
        try
        {
            fileSystem.CreateDirectory(staging);
            var payload = pathService.Combine(staging, "payload");
            await CopyTreeAsync(sessionRoot, payload, cancellationToken).ConfigureAwait(false);

            var manifest = new SessionBundleManifest(
                SchemaVersion: "1.0",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                WorkspaceHint: workspace,
                SessionId: session.Id,
                SessionSnapshotRelativePath: "payload/session.json",
                EventsRelativePath: "payload/events.ndjson",
                CheckpointsDirectoryRelativePath: "payload/checkpoints",
                MutationsDirectoryRelativePath: "payload/mutations",
                ExtraNotes: "Portable SharpClaw session bundle (offline). Redaction is left to callers.");

            var manifestPath = pathService.Combine(staging, "bundle-manifest.json");
            await fileSystem.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(manifest, ProtocolJsonContext.Default.SessionBundleManifest),
                    cancellationToken)
                .ConfigureAwait(false);

            var bundleDir = pathService.Combine(workspace, ".sharpclaw", "exports");
            fileSystem.CreateDirectory(bundleDir);
            var zipPath = string.IsNullOrWhiteSpace(outputZipPath)
                ? pathService.Combine(bundleDir, $"{session.Id}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}.sharpclaw-bundle.zip")
                : pathService.GetFullPath(outputZipPath);

            fileSystem.TryDeleteFile(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            return zipPath;
        }
        finally
        {
            if (fileSystem.DirectoryExists(staging))
            {
                fileSystem.DeleteDirectoryRecursive(staging);
            }
        }
    }

    /// <inheritdoc />
    public async Task<PortableBundleImportResult> ImportBundleZipAsync(
        string workspacePath,
        string bundleZipPath,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var workspace = pathService.GetFullPath(workspacePath);
        var zip = pathService.GetFullPath(bundleZipPath);
        if (!fileSystem.FileExists(zip))
        {
            throw new FileNotFoundException("Bundle zip was not found.", zip);
        }

        var extractRoot = pathService.Combine(pathService.GetTempPath(), $"sharpclaw-import-{Guid.NewGuid():N}");
        fileSystem.CreateDirectory(extractRoot);
        try
        {
            ZipFile.ExtractToDirectory(zip, extractRoot);

            var manifestPath = pathService.Combine(extractRoot, "bundle-manifest.json");
            var manifestJson = await fileSystem.ReadAllTextIfExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                throw new InvalidOperationException("Bundle is missing bundle-manifest.json.");
            }

            var manifest = JsonSerializer.Deserialize(manifestJson, ProtocolJsonContext.Default.SessionBundleManifest)
                ?? throw new InvalidOperationException("Bundle manifest could not be read.");
            if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unsupported bundle schema '{manifest.SchemaVersion}'. Expected '1.0'.");
            }

            var snapshotRelative = NormalizeRelativePath(manifest.SessionSnapshotRelativePath);
            var snapshotFull = pathService.Combine(extractRoot, snapshotRelative);
            if (!fileSystem.FileExists(snapshotFull))
            {
                throw new InvalidOperationException(
                    $"Bundle is missing session snapshot at '{manifest.SessionSnapshotRelativePath}'.");
            }

            var snapshotText = await fileSystem.ReadAllTextIfExistsAsync(snapshotFull, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Session snapshot file was empty.");
            var session = JsonSerializer.Deserialize(snapshotText, ProtocolJsonContext.Default.ConversationSession)
                ?? throw new InvalidOperationException("Session snapshot could not be deserialized.");
            if (string.IsNullOrWhiteSpace(session.Id)
                || !string.Equals(session.Id, manifest.SessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Session snapshot id does not match bundle manifest.");
            }

            var payloadDir = pathService.GetFullPath(
                Path.GetDirectoryName(snapshotFull)
                ?? throw new InvalidOperationException("Could not resolve payload directory from manifest paths."));

            var targetRoot = SessionStorageLayout.GetSessionRoot(pathService, workspace, manifest.SessionId);
            if (fileSystem.DirectoryExists(targetRoot))
            {
                if (!replaceExisting)
                {
                    throw new InvalidOperationException(
                        $"Session '{manifest.SessionId}' already exists in this workspace. Re-run with replace enabled to overwrite.");
                }

                fileSystem.DeleteDirectoryRecursive(targetRoot);
            }

            var sessionsRoot = SessionStorageLayout.GetSessionsRoot(pathService, workspace);
            fileSystem.CreateDirectory(sessionsRoot);

            await CopyTreeAsync(payloadDir, targetRoot, cancellationToken).ConfigureAwait(false);

            return new PortableBundleImportResult(
                manifest.SessionId,
                manifest.SchemaVersion,
                manifest.WorkspaceHint);
        }
        finally
        {
            fileSystem.DeleteDirectoryRecursive(extractRoot);
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var trimmed = relativePath.Trim();
        return trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private async Task CopyTreeAsync(string sourceDir, string destDir, CancellationToken cancellationToken)
    {
        fileSystem.CreateDirectory(destDir);

        foreach (var file in fileSystem.EnumerateFiles(sourceDir, "*"))
        {
            var fileName = Path.GetFileName(file);
            var targetFile = pathService.Combine(destDir, fileName);
            await fileSystem.CopyFileAsync(file, targetFile, cancellationToken).ConfigureAwait(false);
        }

        foreach (var directory in fileSystem.EnumerateDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(dirName))
            {
                continue;
            }

            await CopyTreeAsync(directory, pathService.Combine(destDir, dirName), cancellationToken).ConfigureAwait(false);
        }
    }
}
