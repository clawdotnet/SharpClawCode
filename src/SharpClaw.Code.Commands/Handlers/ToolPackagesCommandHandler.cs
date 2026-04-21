using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists and installs packaged tool bundles for the current workspace.
/// </summary>
public sealed class ToolPackagesCommandHandler(
    IToolPackageService toolPackageService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "tool-packages";

    /// <inheritdoc />
    public string Description => "Lists and installs packaged third-party tool bundles.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildInstallCommand(globalOptions));
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists installed packaged tool bundles.");
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildInstallCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("install", "Installs a packaged tool manifest.");
        var manifestOption = new Option<string>("--manifest")
        {
            Required = true,
            Description = "Path to a serialized ToolPackageManifest JSON file."
        };
        var installSourceOption = new Option<string>("--install-source")
        {
            Description = "Install-source label recorded with the package.",
            DefaultValueFactory = _ => "cli",
        };
        var sourceReferenceOption = new Option<string?>("--source-reference")
        {
            Description = "Optional package directory, binary path, or .nupkg source reference."
        };
        var packageSourceOption = new Option<string?>("--package-source")
        {
            Description = "Optional NuGet source feed URL."
        };
        var disableOption = new Option<bool>("--disable")
        {
            Description = "Install the package without enabling its plugin surface."
        };

        command.Options.Add(manifestOption);
        command.Options.Add(installSourceOption);
        command.Options.Add(sourceReferenceOption);
        command.Options.Add(packageSourceOption);
        command.Options.Add(disableOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var manifestPath = parseResult.GetValue(manifestOption) ?? throw new InvalidOperationException("The --manifest option is required.");
            var installSource = parseResult.GetValue(installSourceOption) ?? "cli";
            var sourceReference = parseResult.GetValue(sourceReferenceOption);
            var packageSource = parseResult.GetValue(packageSourceOption);
            var disable = parseResult.GetValue(disableOption);
            return await ExecuteInstallAsync(
                manifestPath,
                installSource,
                sourceReference,
                packageSource,
                disable,
                context,
                cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    private async Task<int> ExecuteListAsync(
        SharpClaw.Code.Commands.Models.CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var packages = await toolPackageService.ListInstalledAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            packages.Count == 0 ? "No tool packages are installed for this workspace." : $"{packages.Count} tool package(s).",
            JsonSerializer.Serialize(packages.ToList(), ProtocolJsonContext.Default.ListInstalledToolPackage));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteInstallAsync(
        string manifestPath,
        string installSource,
        string? sourceReference,
        string? packageSource,
        bool disable,
        SharpClaw.Code.Commands.Models.CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize(manifestJson, ProtocolJsonContext.Default.ToolPackageManifest)
            ?? throw new InvalidOperationException($"Manifest '{manifestPath}' could not be parsed.");
        var resolvedSourceReference = ResolveSourceReference(manifestPath, sourceReference, manifest.Package.PackageType);
        var installed = await toolPackageService
            .InstallAsync(
                context.WorkingDirectory,
                new ToolPackageInstallRequest(
                    Manifest: manifest,
                    InstallSource: installSource,
                    EnableAfterInstall: !disable,
                    SourceReference: resolvedSourceReference,
                    PackageSource: packageSource),
                cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Installed tool package '{installed.Manifest.Package.PackageId}' ({installed.Manifest.Package.Version}).",
            JsonSerializer.Serialize(installed, ProtocolJsonContext.Default.InstalledToolPackage));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static string? ResolveSourceReference(string manifestPath, string? sourceReference, string packageType)
    {
        if (!string.IsNullOrWhiteSpace(sourceReference))
        {
            return sourceReference;
        }

        return string.Equals(packageType, "local", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            : null;
    }
}
