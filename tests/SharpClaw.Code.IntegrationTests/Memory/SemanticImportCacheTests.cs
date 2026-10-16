using System.Xml.Linq;
using FluentAssertions;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.IntegrationTests.Memory;

/// <summary>Verifies cache invalidation for imported build files outside the root.</summary>
public sealed class SemanticImportCacheTests
{
    /// <summary>Literal external imports are fingerprinted, requiring fresh approval after changes.</summary>
    [Fact]
    public async Task External_literal_import_change_invalidates_approved_snapshot()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var external = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".props");
        try
        {
            await File.WriteAllTextAsync(external, "<Project />");
            var project = Path.Combine(fixture.Root, "Library", "Library.csproj");
            var xml = XDocument.Load(project); xml.Root!.Add(new XElement("Import", new XAttribute("Project", external))); xml.Save(project);
            var request = new DotNetWorkspaceRequest(fixture.Root);
            (await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.Ready);
            (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.Ready);
            await File.WriteAllTextAsync(external, "<Project><PropertyGroup><DefineConstants>EXTERNAL_CHANGED</DefineConstants></PropertyGroup></Project>");
            (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
        }
        finally { File.Delete(external); }
    }

    /// <summary>Property-driven imports are conservatively reauthorized rather than silently reused.</summary>
    [Fact]
    public async Task Property_driven_import_requires_fresh_evaluation_authorization()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var project = Path.Combine(fixture.Root, "Library", "Library.csproj");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Library", "Custom.props"), "<Project />");
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("PropertyGroup", new XElement("CustomProps", "Custom.props")), new XElement("Import", new XAttribute("Project", "$(CustomProps)"))); xml.Save(project);
        var request = new DotNetWorkspaceRequest(fixture.Root);
        (await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.Ready);
        (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
    }
}
