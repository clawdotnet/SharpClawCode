using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Memory;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.MemorySkillsGit;

/// <summary>Verifies non-executing target selection and indexed-search independence.</summary>
public sealed class DotNetWorkspaceTargetResolverTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    [InlineData(".csproj")]
    public async Task Single_supported_target_is_selected(string extension)
    {
        using var workspace = new TemporaryWorkspace();
        var target = Path.Combine(workspace.Root, "Target" + extension);
        await File.WriteAllTextAsync(target, "<Project />");
        var result = await workspace.Resolver.ResolveAsync(new(workspace.Root), CancellationToken.None);
        result.Status.Should().Be(DotNetWorkspaceStatus.Ready);
        result.TargetPath.Should().Be(new PathService().GetCanonicalFullPath(target));
    }

    [Fact]
    public async Task Multiple_targets_are_ambiguous_and_an_explicit_target_disambiguates()
    {
        using var workspace = new TemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "B.slnx"), "<Solution />");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "A.sln"), "");
        var ambiguous = await workspace.Resolver.ResolveAsync(new(workspace.Root), CancellationToken.None);
        ambiguous.Status.Should().Be(DotNetWorkspaceStatus.AmbiguousTarget);
        ambiguous.Candidates.Should().Equal("A.sln", "B.slnx");
        (await workspace.Resolver.ResolveAsync(new(workspace.Root, "B.slnx"), CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.Ready);
    }

    [Fact]
    public async Task A_solution_takes_precedence_over_projects_and_nested_projects_are_not_guessed()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "nested", "Nested.csproj"), "<Project />");
        (await workspace.Resolver.ResolveAsync(new(workspace.Root), CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.NoTarget);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Fixture.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Fixture.slnx"), "<Solution />");
        (await workspace.Resolver.ResolveAsync(new(workspace.Root), CancellationToken.None)).TargetPath.Should().EndWith("Fixture.slnx");
    }

    [Fact]
    public async Task Targets_outside_workspace_and_unsupported_extensions_are_rejected()
    {
        using var workspace = new TemporaryWorkspace();
        (await workspace.Resolver.ResolveAsync(new(workspace.Root, "../Outside.csproj"), CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.InvalidTarget);
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Target.txt"), "");
        (await workspace.Resolver.ResolveAsync(new(workspace.Root, "Target.txt"), CancellationToken.None)).Status.Should().Be(DotNetWorkspaceStatus.InvalidTarget);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_target_enumeration()
    {
        using var workspace = new TemporaryWorkspace();
        var act = () => workspace.Resolver.ResolveAsync(new(workspace.Root), new CancellationToken(true));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Indexed_search_does_not_load_or_evaluate_a_dotnet_workspace()
    {
        using var workspace = new TemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Library.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Invoice.cs"), "namespace Billing; public interface IInvoiceService {} ");
        using var services = new ServiceCollection().AddSharpClawMemory().BuildServiceProvider();
        await services.GetRequiredService<IWorkspaceIndexService>().RefreshAsync(workspace.Root, CancellationToken.None);
        var result = await services.GetRequiredService<IWorkspaceSearchService>().SearchAsync(workspace.Root, new("IInvoiceService", null), CancellationToken.None);
        result.Hits.Should().NotBeEmpty();
        Directory.Exists(Path.Combine(workspace.Root, "obj")).Should().BeFalse();
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpclaw-target-tests-" + Guid.NewGuid().ToString("N"));
        internal DotNetWorkspaceTargetResolver Resolver { get; } = new(new LocalFileSystem(), new PathService());
        internal TemporaryWorkspace() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
