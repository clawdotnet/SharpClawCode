using System.Text;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Tools.Services;

/// <summary>
/// Persists packaged-tool manifests and maps them onto the existing plugin execution pipeline.
/// </summary>
public sealed class ToolPackageService(
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    IPluginManager pluginManager) : IToolPackageService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledToolPackage>> ListInstalledAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var packagesRoot = storagePathResolver.GetToolPackagesRoot(normalizedWorkspace);
        if (!fileSystem.DirectoryExists(packagesRoot))
        {
            return [];
        }

        var packages = new List<InstalledToolPackage>();
        foreach (var manifestPath in fileSystem.EnumerateFiles(packagesRoot, "*.json"))
        {
            var content = await fileSystem.ReadAllTextIfExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var installed = JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.InstalledToolPackage);
            if (installed is not null)
            {
                packages.Add(installed);
            }
        }

        return packages
            .OrderByDescending(static package => package.InstalledAtUtc)
            .ThenBy(static package => package.Manifest.Package.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<InstalledToolPackage> InstallAsync(
        string workspaceRoot,
        ToolPackageInstallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Manifest);

        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var packagesRoot = storagePathResolver.GetToolPackagesRoot(normalizedWorkspace);
        fileSystem.CreateDirectory(packagesRoot);

        var installed = new InstalledToolPackage(
            Manifest: request.Manifest,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            InstallSource: request.InstallSource.Trim());
        var path = pathService.Combine(packagesRoot, $"{SanitizeFileName(request.Manifest.Package.PackageId)}.json");
        await fileSystem
            .WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(installed, ProtocolJsonContext.Default.InstalledToolPackage),
                cancellationToken)
            .ConfigureAwait(false);

        var pluginManifest = ToPluginManifest(request.Manifest);
        await pluginManager
            .InstallAsync(
                normalizedWorkspace,
                new PluginInstallRequest(
                    pluginManifest,
                    JsonSerializer.Serialize(request.Manifest, ProtocolJsonContext.Default.ToolPackageManifest)),
                cancellationToken)
            .ConfigureAwait(false);
        if (request.EnableAfterInstall)
        {
            await pluginManager.EnableAsync(normalizedWorkspace, pluginManifest.Id, cancellationToken).ConfigureAwait(false);
        }

        return installed;
    }

    private static PluginManifest ToPluginManifest(ToolPackageManifest manifest)
        => new(
            Id: manifest.Package.PackageId,
            Name: manifest.Package.PackageId,
            Version: manifest.Package.Version,
            Description: manifest.Description,
            EntryPoint: manifest.Package.EntryAssembly,
            Arguments: [],
            Capabilities:
            [
                "tool-package",
                manifest.Package.PackageType,
            ],
            Tools: manifest.Tools.Select(tool => new PluginToolDescriptor(
                Name: tool.Name,
                Description: tool.Description,
                InputDescription: string.IsNullOrWhiteSpace(tool.InputSchemaJson) ? "{}" : tool.InputSchemaJson!,
                Tags: tool.Tags ?? manifest.Package.Tags,
                IsDestructive: tool.IsDestructive,
                RequiresApproval: tool.RequiresApproval,
                SourcePluginId: manifest.Package.PackageId,
                InputTypeName: "json",
                InputSchemaJson: tool.InputSchemaJson,
                Trust: ResolveTrust(manifest.Package.PackageType))).ToArray(),
            Trust: ResolveTrust(manifest.Package.PackageType),
            PublisherId: manifest.PublisherId,
            SignatureHint: $"{manifest.Package.PackageType}:{manifest.Package.PackageId}:{manifest.Package.Version}");

    private static PluginTrustLevel ResolveTrust(string packageType)
        => string.Equals(packageType, "local", StringComparison.OrdinalIgnoreCase)
            ? PluginTrustLevel.WorkspaceTrusted
            : PluginTrustLevel.Untrusted;

    private static void Validate(ToolPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Package.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Package.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Package.EntryAssembly);
        if (manifest.Tools.Length == 0)
        {
            throw new InvalidOperationException("Tool packages must declare at least one tool.");
        }

        var duplicate = manifest.Tools
            .GroupBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Tool package '{manifest.Package.PackageId}' declares duplicate tool '{duplicate.Key}'.");
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '-' : character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "tool-package" : result;
    }
}
