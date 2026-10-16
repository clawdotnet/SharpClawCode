using FluentAssertions;
using System.Xml.Linq;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.IntegrationTests.Memory;

/// <summary>Protects approved loading, evaluated project properties, SDK generators, and rename validation.</summary>
public sealed class SemanticReviewRegressionTests
{
    /// <summary>SDK generators supply real symbols, and generated references cannot cause an unsafe successful rename.</summary>
    [Fact]
    public async Task Sdk_generators_supply_diagnostics_and_rename_is_validated_against_generated_members()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Library", "Serialization.cs"), """
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.RegularExpressions;
            namespace Billing;
            public sealed record Shipment(int Id);
            [JsonSerializable(typeof(Shipment))]
            public partial class ShipmentJsonContext : JsonSerializerContext { }
            public static partial class Expressions
            {
                [GeneratedRegex("invoice")] public static partial Regex InvoicePattern();
                public static string Serialize(Shipment item) => JsonSerializer.Serialize(item, ShipmentJsonContext.Default.Shipment);
            }
            """);
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var diagnostics = await fixture.Service.GetDiagnosticsAsync(request, new(Severity: "error"), fixture.Authorization, CancellationToken.None);
        diagnostics.Diagnostics.Should().BeEmpty(string.Join(";", diagnostics.Diagnostics.Select(item => item.Message)));
        var plan = await fixture.Service.PlanRenameAsync(request, new(Name: "Shipment", NewName: "Delivery"), null, CancellationToken.None);
        if (plan.Errors.Length > 0) { plan.Changes.Should().BeEmpty(); return; }
        plan.Changes.Should().NotBeEmpty();
        foreach (var change in plan.Changes) await File.WriteAllTextAsync(Path.Combine(fixture.Root, change.Path), change.ContentAfter);
        var build = await new ProcessRunner(new SystemClock()).RunAsync(new("dotnet", ["build", "Fixture.slnx", "--no-restore", "--disable-build-servers"], fixture.Root, null), CancellationToken.None);
        build.ExitCode.Should().Be(0, build.StandardOutput + build.StandardError);
    }

    /// <summary>Properties imported from Directory.Build.props decide test classification and redirected asset invalidation.</summary>
    [Fact]
    public async Task Evaluated_test_flags_and_redirected_assets_are_honored_and_fingerprinted()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Directory.Build.props"), """
            <Project><PropertyGroup>
              <UseArtifactsOutput>true</UseArtifactsOutput>
              <IsTestProject>false</IsTestProject>
            </PropertyGroup></Project>
            """);
        var projectPath = Path.Combine(fixture.Root, "Fixture.Tests", "Fixture.Tests.csproj");
        await File.WriteAllTextAsync(projectPath, (await File.ReadAllTextAsync(projectPath)).Replace("<IsTestProject>true</IsTestProject>", "<IsTestProject>false</IsTestProject>"));
        await fixture.RestoreAsync();
        // Remove legacy assets to prove the loader uses the evaluated path.
        foreach (var project in new[] { "Library", "Consumer", "Fixture.Tests" }) Directory.Delete(Path.Combine(fixture.Root, project, "obj"), true);
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var summary = await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None);
        summary.Status.Should().Be(DotNetWorkspaceStatus.Ready, string.Join(";", summary.Messages));
        summary.Projects.Should().OnlyContain(project => !project.IsTestProject);
        (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.Ready);
        var assets = Directory.GetFiles(Path.Combine(fixture.Root, "artifacts"), "project.assets.json", SearchOption.AllDirectories).First();
        await File.AppendAllTextAsync(assets, "\n");
        (await fixture.Service.InspectSolutionAsync(request, null, CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
    }

    /// <summary>A pending human decision neither blocks unrelated calls nor prevents disposal.</summary>
    [Fact]
    public async Task Pending_authorization_releases_global_gate_and_does_not_block_disposal()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var approval = new PendingAuthorization();
        var pending = fixture.Service.InspectSolutionAsync(new(fixture.Root), approval, CancellationToken.None);
        await approval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var other = await fixture.Service.InspectSolutionAsync(new(fixture.Root, "Library/Library.csproj"), null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            other.Status.Should().Be(DotNetWorkspaceStatus.PermissionDenied);
            await Task.Run(fixture.Service.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { approval.Answer.TrySetResult(true); }
        Func<Task> completed = async () => await pending;
        await completed.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>Unrelated inaccessible directories do not break the source fingerprint.</summary>
    [Fact]
    public async Task Inaccessible_directory_is_skipped_during_fingerprinting()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var inaccessible = Path.Combine(fixture.Root, "pgdata");
        Directory.CreateDirectory(inaccessible);
        var files = new FaultingFileSystem { BeforeEnumerate = path => { if (path.EndsWith("pgdata", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Injected unreadable bind mount"); } };
        var paths = new PathService();
        using var service = new DotNetWorkspaceSemanticService(files, paths, new DotNetWorkspaceTargetResolver(files, paths));
        var result = await service.InspectSolutionAsync(new(fixture.Root), fixture.Authorization, CancellationToken.None);
        result.Status.Should().Be(DotNetWorkspaceStatus.Ready, string.Join(";", result.Messages));
    }

    /// <summary>Existing errors whose messages change with the symbol do not falsely reject a rename.</summary>
    [Fact]
    public async Task Rename_matches_existing_errors_by_mapped_location_instead_of_message()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        await File.AppendAllTextAsync(Path.Combine(fixture.Root, "Library", "Invoice.cs"), "\npublic sealed class BrokenUse { public void Bad() { IInvoiceService service = null!; service.Missing(); } }\n");
        var plan = await fixture.Service.PlanRenameAsync(new(fixture.Root), new(Name: "IInvoiceService", NewName: "ILongerBillingService"), fixture.Authorization, CancellationToken.None);
        plan.Errors.Should().BeEmpty();
        plan.Changes.Should().NotBeEmpty();
    }

    /// <summary>Excluded custom analyzer references make limitations visible and disable unverified rename.</summary>
    [Fact]
    public async Task Custom_analyzers_are_excluded_reported_and_block_rename()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var project = Path.Combine(fixture.Root, "Library", "Library.csproj");
        var xml = XDocument.Load(project);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Include", typeof(SemanticReviewRegressionTests).Assembly.Location))));
        xml.Save(project);
        var diagnostics = await fixture.Service.GetDiagnosticsAsync(new(fixture.Root), new(Severity: "error"), fixture.Authorization, CancellationToken.None);
        diagnostics.Messages.Should().Contain(message => message.Contains("excluded", StringComparison.Ordinal));
        var plan = await fixture.Service.PlanRenameAsync(new(fixture.Root), new(Name: "IInvoiceService", NewName: "IBillingService"), null, CancellationToken.None);
        plan.Changes.Should().BeEmpty();
        plan.Errors.Should().Contain(error => error.Contains("cannot be verified", StringComparison.Ordinal));
    }

    private sealed class PendingAuthorization : IDotNetWorkspaceEvaluationAuthorization
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> AuthorizeAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken) { Entered.TrySetResult(); return Answer.Task.WaitAsync(cancellationToken); }
    }
}
