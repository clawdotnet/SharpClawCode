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
    public async Task Tool_package_service_should_install_and_list_workspace_packages()
    {
        Directory.CreateDirectory(workspaceRoot);
        var services = new ServiceCollection();
        services.AddSharpClawTools();
        using var serviceProvider = services.BuildServiceProvider();

        var packageService = serviceProvider.GetRequiredService<IToolPackageService>();
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();
        var request = new ToolPackageInstallRequest(
            new ToolPackageManifest(
                new ToolPackageReference("acme.widgets", "1.2.3", "local", "widget-tool"),
                "acme",
                "Widget helper tools",
                [new PackagedToolDescriptor("widget_lookup", "Looks up widgets", """{"type":"object"}""", Tags: ["widgets"])]),
            InstallSource: "unit-test",
            EnableAfterInstall: false);

        var installed = await packageService.InstallAsync(workspaceRoot, request, CancellationToken.None);
        var listed = await packageService.ListInstalledAsync(workspaceRoot, CancellationToken.None);
        var plugins = await pluginManager.ListAsync(workspaceRoot, CancellationToken.None);

        installed.Manifest.Package.PackageId.Should().Be("acme.widgets");
        listed.Should().ContainSingle(package => package.Manifest.Package.PackageId == "acme.widgets");
        plugins.Should().ContainSingle(plugin => plugin.Descriptor.Id == "acme.widgets");
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }
}
