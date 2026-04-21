using System.Text;
using System.Text.Json;
using System.IO.Compression;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
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
    IProcessRunner processRunner,
    IPluginManager pluginManager) : IToolPackageService
{
    private const string SupportedTargetFramework = "net10.0";

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

        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        Validate(request.Manifest);
        await EnsureNoToolNameConflictsAsync(normalizedWorkspace, request.Manifest, cancellationToken).ConfigureAwait(false);
        var resolvedInstall = await ResolveInstallAsync(normalizedWorkspace, request, cancellationToken).ConfigureAwait(false);
        var packagesRoot = storagePathResolver.GetToolPackagesRoot(normalizedWorkspace);
        fileSystem.CreateDirectory(packagesRoot);

        var installed = new InstalledToolPackage(
            Manifest: request.Manifest,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            InstallSource: request.InstallSource.Trim(),
            ResolvedInstall: resolvedInstall);
        var path = pathService.Combine(packagesRoot, $"{SanitizeFileName(request.Manifest.Package.PackageId)}.json");
        await fileSystem
            .WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(installed, ProtocolJsonContext.Default.InstalledToolPackage),
                cancellationToken)
            .ConfigureAwait(false);

        var pluginManifest = ToPluginManifest(request.Manifest, resolvedInstall);
        await pluginManager
            .InstallAsync(
                normalizedWorkspace,
                new PluginInstallRequest(
                    pluginManifest,
                    JsonSerializer.Serialize(installed, ProtocolJsonContext.Default.InstalledToolPackage)),
                cancellationToken)
            .ConfigureAwait(false);
        if (request.EnableAfterInstall)
        {
            await pluginManager.EnableAsync(normalizedWorkspace, pluginManifest.Id, cancellationToken).ConfigureAwait(false);
        }

        return installed;
    }

    private static PluginManifest ToPluginManifest(ToolPackageManifest manifest, ToolPackageResolvedInstall resolvedInstall)
        => new(
            Id: manifest.Package.PackageId,
            Name: manifest.Package.PackageId,
            Version: manifest.Package.Version,
            Description: manifest.Description,
            EntryPoint: SelectPluginEntryPoint(resolvedInstall),
            Arguments: SelectPluginArguments(resolvedInstall),
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
        if (!string.IsNullOrWhiteSpace(manifest.Package.TargetFramework)
            && !string.Equals(manifest.Package.TargetFramework, SupportedTargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Tool package '{manifest.Package.PackageId}' targets '{manifest.Package.TargetFramework}', but this runtime only supports '{SupportedTargetFramework}'.");
        }

        if (manifest.Tools.Length == 0)
        {
            throw new InvalidOperationException("Tool packages must declare at least one tool.");
        }

        foreach (var tool in manifest.Tools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.Description);
        }

        var duplicate = manifest.Tools
            .GroupBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Tool package '{manifest.Package.PackageId}' declares duplicate tool '{duplicate.Key}'.");
        }
    }

    private async Task EnsureNoToolNameConflictsAsync(
        string workspaceRoot,
        ToolPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var installed = await ListInstalledAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var existingToolNames = installed
            .Where(package => !string.Equals(package.Manifest.Package.PackageId, manifest.Package.PackageId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static package => package.Manifest.Tools)
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflictingTool = manifest.Tools.FirstOrDefault(tool => existingToolNames.Contains(tool.Name));
        if (conflictingTool is not null)
        {
            throw new InvalidOperationException(
                $"Tool '{conflictingTool.Name}' is already installed from another package and cannot be activated twice.");
        }
    }

    private async Task<ToolPackageResolvedInstall> ResolveInstallAsync(
        string workspaceRoot,
        ToolPackageInstallRequest request,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Manifest.Package.PackageType, "nuget", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveNugetInstallAsync(workspaceRoot, request, cancellationToken).ConfigureAwait(false);
        }

        var resolvedEntryAssembly = ResolveLocalEntryAssembly(request.Manifest.Package.EntryAssembly, request.SourceReference);
        return new ToolPackageResolvedInstall(
            SourceReference: request.SourceReference,
            PackageSource: request.PackageSource,
            PackageFilePath: null,
            ExtractedPackageRoot: null,
            ResolvedEntryAssembly: resolvedEntryAssembly,
            ResolvedEntryArguments: request.Manifest.Package.EntryArguments);
    }

    private async Task<ToolPackageResolvedInstall> ResolveNugetInstallAsync(
        string workspaceRoot,
        ToolPackageInstallRequest request,
        CancellationToken cancellationToken)
    {
        var packageId = request.Manifest.Package.PackageId;
        var version = request.Manifest.Package.Version;
        var packagesRoot = storagePathResolver.GetToolPackagesRoot(workspaceRoot);
        fileSystem.CreateDirectory(packagesRoot);

        var packageFilePath = pathService.Combine(packagesRoot, $"{SanitizeFileName(packageId)}-{SanitizeFileName(version)}.nupkg");
        if (!string.IsNullOrWhiteSpace(request.SourceReference)
            && Path.GetExtension(request.SourceReference) is ".nupkg"
            && fileSystem.FileExists(request.SourceReference))
        {
            await fileSystem.CopyFileAsync(request.SourceReference, packageFilePath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var downloadRoot = pathService.Combine(packagesRoot, ".downloads", $"{SanitizeFileName(packageId)}-{Guid.NewGuid():N}");
            fileSystem.CreateDirectory(downloadRoot);
            var arguments = new List<string>
            {
                "nuget",
                "download",
                packageId,
                "--version",
                version,
                "--output",
                downloadRoot,
            };
            if (!string.IsNullOrWhiteSpace(request.PackageSource))
            {
                arguments.Add("--source");
                arguments.Add(request.PackageSource!);
            }

            var result = await processRunner
                .RunAsync(
                    new ProcessRunRequest(
                        FileName: "dotnet",
                        Arguments: [.. arguments],
                        WorkingDirectory: workspaceRoot,
                        EnvironmentVariables: null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"dotnet nuget download failed for '{packageId}'."
                        : result.StandardError.Trim());
            }

            var downloadedPackage = fileSystem.EnumerateFiles(downloadRoot, "*.nupkg").FirstOrDefault()
                ?? throw new InvalidOperationException($"No .nupkg was downloaded for '{packageId}'.");
            await fileSystem.CopyFileAsync(downloadedPackage, packageFilePath, cancellationToken).ConfigureAwait(false);
            fileSystem.DeleteDirectoryRecursive(downloadRoot);
        }

        var extractedPackageRoot = storagePathResolver.GetExtractedToolPackageRoot(workspaceRoot, packageId, version);
        if (fileSystem.DirectoryExists(extractedPackageRoot))
        {
            fileSystem.DeleteDirectoryRecursive(extractedPackageRoot);
        }

        fileSystem.CreateDirectory(extractedPackageRoot);
        ZipFile.ExtractToDirectory(packageFilePath, extractedPackageRoot, overwriteFiles: true);

        var resolvedEntryAssembly = ResolveExtractedEntryAssembly(request.Manifest.Package.EntryAssembly, extractedPackageRoot);
        EnsureUnixExecuteBit(resolvedEntryAssembly);
        return new ToolPackageResolvedInstall(
            SourceReference: request.SourceReference,
            PackageSource: request.PackageSource,
            PackageFilePath: packageFilePath,
            ExtractedPackageRoot: extractedPackageRoot,
            ResolvedEntryAssembly: resolvedEntryAssembly,
            ResolvedEntryArguments: request.Manifest.Package.EntryArguments);
    }

    private string ResolveLocalEntryAssembly(string entryAssembly, string? sourceReference)
    {
        if (Path.IsPathRooted(entryAssembly) || string.IsNullOrWhiteSpace(sourceReference))
        {
            return entryAssembly;
        }

        if (fileSystem.DirectoryExists(sourceReference))
        {
            return pathService.GetFullPath(pathService.Combine(sourceReference, entryAssembly));
        }

        var sourceDirectory = Path.GetDirectoryName(sourceReference);
        return string.IsNullOrWhiteSpace(sourceDirectory)
            ? entryAssembly
            : pathService.GetFullPath(pathService.Combine(sourceDirectory, entryAssembly));
    }

    private string ResolveExtractedEntryAssembly(string entryAssembly, string extractedPackageRoot)
    {
        if (Path.IsPathRooted(entryAssembly))
        {
            return entryAssembly;
        }

        return pathService.GetFullPath(pathService.Combine(extractedPackageRoot, entryAssembly));
    }

    private static string SelectPluginEntryPoint(ToolPackageResolvedInstall resolvedInstall)
        => string.Equals(Path.GetExtension(resolvedInstall.ResolvedEntryAssembly), ".dll", StringComparison.OrdinalIgnoreCase)
            ? "dotnet"
            : resolvedInstall.ResolvedEntryAssembly;

    private static string[] SelectPluginArguments(ToolPackageResolvedInstall resolvedInstall)
    {
        if (string.Equals(Path.GetExtension(resolvedInstall.ResolvedEntryAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return [resolvedInstall.ResolvedEntryAssembly, .. resolvedInstall.ResolvedEntryArguments ?? []];
        }

        return resolvedInstall.ResolvedEntryArguments ?? [];
    }

    private static void EnsureUnixExecuteBit(string path)
    {
        if (OperatingSystem.IsWindows()
            || string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }
        catch (PlatformNotSupportedException)
        {
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
