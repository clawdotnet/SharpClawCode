using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.UnitTests.Tools;

/// <summary>
/// Verifies packaged tool manifests install into the workspace catalog and map onto plugins.
/// </summary>
public sealed class ToolPackageServiceTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-tool-packages", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Tool_package_service_should_install_local_packages_with_resolved_entry_metadata()
    {
        Directory.CreateDirectory(workspaceRoot);
        var sourceRoot = CreatePackageSource("local", "bin/widget-tool.dll");
        using var serviceProvider = CreateServiceProvider();

        var packageService = serviceProvider.GetRequiredService<IToolPackageService>();
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();
        var request = new ToolPackageInstallRequest(
            new ToolPackageManifest(
                new ToolPackageReference(
                    "acme.widgets",
                    "1.2.3",
                    "local",
                    "bin/widget-tool.dll",
                    EntryArguments: ["--mode", "serve"],
                    TargetFramework: "net10.0"),
                "acme",
                "Widget helper tools",
                [new PackagedToolDescriptor("widget_lookup", "Looks up widgets", """{"type":"object"}""", Tags: ["widgets"])]),
            InstallSource: "unit-test",
            EnableAfterInstall: false,
            SourceReference: sourceRoot);

        var installed = await packageService.InstallAsync(workspaceRoot, request, CancellationToken.None);
        var listed = await packageService.ListInstalledAsync(workspaceRoot, CancellationToken.None);
        var plugins = await pluginManager.ListAsync(workspaceRoot, CancellationToken.None);

        installed.Manifest.Package.PackageId.Should().Be("acme.widgets");
        installed.ResolvedInstall.Should().NotBeNull();
        installed.ResolvedInstall!.ResolvedEntryAssembly.Should().Be(Path.Combine(sourceRoot, "bin", "widget-tool.dll"));
        installed.ResolvedInstall.ResolvedEntryArguments.Should().BeEquivalentTo(["--mode", "serve"]);
        listed.Should().ContainSingle(package => package.Manifest.Package.PackageId == "acme.widgets");
        plugins.Should().ContainSingle(plugin => plugin.Descriptor.Id == "acme.widgets");
    }

    [Fact]
    public async Task Tool_package_service_should_install_nuget_packages_from_local_archives()
    {
        Directory.CreateDirectory(workspaceRoot);
        var archivePath = CreateNuGetArchive("nuget", "tools/echo.sh");
        using var serviceProvider = CreateServiceProvider();

        var packageService = serviceProvider.GetRequiredService<IToolPackageService>();
        var request = new ToolPackageInstallRequest(
            new ToolPackageManifest(
                new ToolPackageReference(
                    "acme.echo",
                    "2.0.0",
                    "nuget",
                    "tools/echo.sh",
                    TargetFramework: "net10.0"),
                "acme",
                "Echo tool package",
                [new PackagedToolDescriptor("echo_tool", "Echoes content", """{"type":"object"}""")]),
            InstallSource: "unit-test",
            EnableAfterInstall: false,
            SourceReference: archivePath,
            PackageSource: "local-archive");

        var installed = await packageService.InstallAsync(workspaceRoot, request, CancellationToken.None);

        installed.ResolvedInstall.Should().NotBeNull();
        installed.ResolvedInstall!.PackageFilePath.Should().NotBeNull();
        installed.ResolvedInstall.ExtractedPackageRoot.Should().NotBeNull();
        installed.ResolvedInstall.ResolvedEntryAssembly.Should().EndWith(Path.Combine("tools", "echo.sh"));
        File.Exists(installed.ResolvedInstall.PackageFilePath!).Should().BeTrue();
        File.Exists(installed.ResolvedInstall.ResolvedEntryAssembly).Should().BeTrue();
    }

    [Fact]
    public async Task Tool_package_service_should_reject_duplicate_tool_names_across_packages()
    {
        Directory.CreateDirectory(workspaceRoot);
        var sourceRoot = CreatePackageSource("conflict", "tool.dll");
        using var serviceProvider = CreateServiceProvider();
        var packageService = serviceProvider.GetRequiredService<IToolPackageService>();

        await packageService.InstallAsync(
            workspaceRoot,
            new ToolPackageInstallRequest(
                new ToolPackageManifest(
                    new ToolPackageReference("acme.first", "1.0.0", "local", "tool.dll", TargetFramework: "net10.0"),
                    "acme",
                    "First package",
                    [new PackagedToolDescriptor("shared_tool", "Shared tool", """{"type":"object"}""")]),
                InstallSource: "unit-test",
                EnableAfterInstall: false,
                SourceReference: sourceRoot),
            CancellationToken.None);

        var act = () => packageService.InstallAsync(
            workspaceRoot,
            new ToolPackageInstallRequest(
                new ToolPackageManifest(
                    new ToolPackageReference("acme.second", "1.0.0", "local", "tool.dll", TargetFramework: "net10.0"),
                    "acme",
                    "Second package",
                    [new PackagedToolDescriptor("shared_tool", "Shared tool again", """{"type":"object"}""")]),
                InstallSource: "unit-test",
                EnableAfterInstall: false,
                SourceReference: sourceRoot),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*shared_tool*already installed*");
    }

    [Fact]
    public async Task Tool_package_service_should_reject_unsupported_target_frameworks()
    {
        Directory.CreateDirectory(workspaceRoot);
        var sourceRoot = CreatePackageSource("framework", "tool.dll");
        using var serviceProvider = CreateServiceProvider();
        var packageService = serviceProvider.GetRequiredService<IToolPackageService>();

        var act = () => packageService.InstallAsync(
            workspaceRoot,
            new ToolPackageInstallRequest(
                new ToolPackageManifest(
                    new ToolPackageReference("acme.legacy", "1.0.0", "local", "tool.dll", TargetFramework: "net8.0"),
                    "acme",
                    "Legacy package",
                    [new PackagedToolDescriptor("legacy_tool", "Legacy tool", """{"type":"object"}""")]),
                InstallSource: "unit-test",
                EnableAfterInstall: false,
                SourceReference: sourceRoot),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*net8.0*net10.0*");
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSharpClawTools();
        return services.BuildServiceProvider();
    }

    private string CreatePackageSource(string name, string relativeEntryAssembly)
    {
        var root = Path.Combine(workspaceRoot, name);
        var fullEntryAssemblyPath = Path.Combine(root, relativeEntryAssembly.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullEntryAssemblyPath)!);
        File.WriteAllText(fullEntryAssemblyPath, "placeholder");
        return root;
    }

    private string CreateNuGetArchive(string name, string relativeEntryAssembly)
    {
        var sourceRoot = CreatePackageSource(name, relativeEntryAssembly);
        var archivePath = Path.Combine(workspaceRoot, $"{name}.nupkg");
        ZipFile.CreateFromDirectory(sourceRoot, archivePath);
        return archivePath;
    }
}
