using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.IntegrationTests.Memory;

/// <summary>Exercises real, offline Roslyn solution loading and exact symbol resolution.</summary>
public sealed class DotNetWorkspaceSemanticTests
{
    /// <summary>Cold load is denied before any design time target executes.</summary>
    [Fact]
    public async Task Cold_load_is_denied_before_any_design_time_target_executes()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var sentinel = Path.Combine(fixture.Root, "Library", "evaluation-sentinel.txt");
        File.Delete(sentinel);
        var result = await fixture.Service.InspectSolutionAsync(new(fixture.Root), null, CancellationToken.None);
        result.Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
        File.Exists(sentinel).Should().BeFalse();
    }

    /// <summary>Approved load inspects graph and resolves an exact interface without analyzer execution.</summary>
    [Fact]
    public async Task Approved_load_inspects_graph_and_resolves_an_exact_interface_without_analyzer_execution()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var summary = await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None);
        summary.Status.Should().Be(DotNetWorkspaceStatus.Ready, string.Join(";", summary.Messages));
        summary.Projects.Should().HaveCount(2);
        summary.Projects.Should().OnlyContain(project => project.TargetFrameworks.Contains("net10.0"));
        summary.ProjectReferences.Should().Contain(new DotNetProjectReference("Consumer/Consumer.csproj", "Library/Library.csproj"));
        File.Exists(Path.Combine(fixture.Root, "Library", "evaluation-sentinel.txt")).Should().BeTrue();
        var result = await fixture.Service.ResolveSymbolAsync(request, new("IInvoiceService", Container: "Billing", Kind: "interface"), null, CancellationToken.None);
        result.Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        result.Symbol!.DocumentationId.Should().Be("T:Billing.IInvoiceService");
        result.Symbol.Locations.Should().ContainSingle().Which.Path.Should().Be("Library/Invoice.cs");
        fixture.Authorization.Calls.Should().Be(1);
        var crossProject = await fixture.Service.ResolveSymbolAsync(request, new("Run", Container: "Consumer.InvoiceConsumer"), null, CancellationToken.None);
        crossProject.Symbol!.DocumentationId.Should().Be("M:Consumer.InvoiceConsumer.Run(Billing.IInvoiceService)");
        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.CSharpSymbolResolutionResult);
        JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.CSharpSymbolResolutionResult).Should().BeEquivalentTo(result);
        json.Should().NotContain("Microsoft.CodeAnalysis");
    }

    /// <summary>Source changes refresh cached symbols without reevaluating project code.</summary>
    [Fact]
    public async Task Source_changes_refresh_cached_symbols_without_reevaluating_project_code()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None);
        var path = Path.Combine(fixture.Root, "Library", "Invoice.cs");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("IInvoiceService", "IBillingService", StringComparison.Ordinal));
        var result = await fixture.Service.ResolveSymbolAsync(request, new("IBillingService"), null, CancellationToken.None);
        result.Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        fixture.Authorization.Calls.Should().Be(1);
        (await fixture.Service.ResolveSymbolAsync(request, new("IInvoiceService"), null, CancellationToken.None)).Status.Should().Be(CSharpSymbolResolutionStatus.NotFound);
    }

    /// <summary>Changed project or compile item requires fresh authorization.</summary>
    [Fact]
    public async Task Changed_project_or_compile_item_requires_fresh_authorization()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Library", "Added.cs"), "namespace Billing; public sealed class Added {}");
        (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
        (await fixture.Service.ResolveSymbolAsync(request, new("Added"), fixture.Authorization, CancellationToken.None)).Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        fixture.Authorization.Calls.Should().Be(2);
    }

    /// <summary>Overloads are ambiguous until a signature selects one.</summary>
    [Fact]
    public async Task Overloads_are_ambiguous_until_a_signature_selects_one()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var result = await fixture.Service.ResolveSymbolAsync(request, new("Process", Container: "Billing.InvoiceService", Kind: "method"), fixture.Authorization, CancellationToken.None);
        result.Status.Should().Be(CSharpSymbolResolutionStatus.Ambiguous);
        result.Symbol.Should().BeNull();
        result.Candidates.Should().HaveCount(2);
        var exact = await fixture.Service.ResolveSymbolAsync(request, new("Process", Signature: result.Candidates[0].Signature), null, CancellationToken.None);
        exact.Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
    }

    /// <summary>Concurrent queries share one authorized load.</summary>
    [Fact]
    public async Task Concurrent_queries_share_one_authorized_load()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => fixture.Service.ResolveSymbolAsync(new(fixture.Root), new("IInvoiceService"), fixture.Authorization, CancellationToken.None)));
        results.Should().OnlyContain(result => result.Status == CSharpSymbolResolutionStatus.Resolved);
        fixture.Authorization.Calls.Should().Be(1);
    }

    /// <summary>Failed loading does not poison the cache.</summary>
    [Fact]
    public async Task Failed_loading_does_not_poison_the_cache()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var path = Path.Combine(fixture.Root, "Library", "Library.csproj");
        var original = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, "<broken");
        var failed = await fixture.Service.InspectSolutionAsync(new(fixture.Root, "Library/Library.csproj"), fixture.Authorization, CancellationToken.None);
        failed.Status.Should().NotBe(DotNetWorkspaceStatus.Ready);
        await File.WriteAllTextAsync(path, original);
        (await fixture.Service.ResolveSymbolAsync(new(fixture.Root, "Library/Library.csproj"), new("IInvoiceService"), fixture.Authorization, CancellationToken.None)).Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
    }

    /// <summary>Cache eviction requires a new load but does not dispose active queries.</summary>
    [Fact]
    public async Task Cache_eviction_requires_a_new_load_but_does_not_dispose_active_queries()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        for (var index = 0; index < 5; index++)
        {
            var result = await fixture.Service.ResolveSymbolAsync(new(fixture.Root, "Library/Library.csproj", "Config" + index), new("IInvoiceService"), fixture.Authorization, CancellationToken.None);
            result.Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        }
        (await fixture.Service.InspectSolutionAsync(new(fixture.Root, "Library/Library.csproj", "Config0"), null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
        (await fixture.Service.ResolveSymbolAsync(new(fixture.Root, "Library/Library.csproj", "Config0"), new("IInvoiceService"), fixture.Authorization, CancellationToken.None)).Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        fixture.Authorization.Calls.Should().Be(6);
    }

    /// <summary>Missing restore assets produce an explicit prerequisite result without restoring.</summary>
    [Fact]
    public async Task Missing_assets_are_reported_without_implicit_restore()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var assets = Path.Combine(fixture.Root, "Library", "obj", "project.assets.json");
        File.Delete(assets);
        var result = await fixture.Service.InspectSolutionAsync(new(fixture.Root, "Library/Library.csproj"), fixture.Authorization, CancellationToken.None);
        result.Status.Should().Be(DotNetWorkspaceStatus.RestoreRequired, string.Join(";", result.Messages));
        File.Exists(assets).Should().BeFalse();
    }

    /// <summary>Workspace SDK selection does not silently ignore an unavailable global.json version.</summary>
    [Fact]
    public async Task Missing_workspace_sdk_is_reported_structurally()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), "{\"sdk\":{\"version\":\"9999.0.100\",\"rollForward\":\"disable\"}}");
        var result = await fixture.Service.InspectSolutionAsync(new(fixture.Root), fixture.Authorization, CancellationToken.None);
        result.Status.Should().Be(DotNetWorkspaceStatus.SdkUnavailable, string.Join(";", result.Messages));
    }
}
