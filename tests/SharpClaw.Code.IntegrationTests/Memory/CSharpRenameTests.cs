using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.IntegrationTests.Memory;

/// <summary>Checks multi-file semantic rename and real durable mutation replay.</summary>
public sealed class CSharpRenameTests
{
    /// <summary>Two successive renames compile and replay as one ordered reversible change set.</summary>
    [Fact]
    public async Task Semantic_rename_compiles_and_durable_undo_redo_preserves_recording_order()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var provider = CreateProvider(fixture);
        var recorder = new TurnMutationAccumulator();
        var context = Context(fixture, recorder);
        var tools = provider.GetRequiredService<IToolExecutor>();
        var paths = new[] { "Library/Invoice.cs", "Consumer/InvoiceConsumer.cs" };
        var original = paths.ToDictionary(path => path, path => File.ReadAllText(Path.Combine(fixture.Root, path)));
        foreach (var (before, after) in new[] { ("IInvoiceService", "IBillingService"), ("IBillingService", "IAccountService") })
        {
            var result = await tools.ExecuteAsync("csharp_rename_symbol", Arguments(before, after), context, CancellationToken.None);
            result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        }
        recorder.ToSnapshot().Should().HaveCount(4);
        recorder.ToSnapshot()[0].ContentAfter.Should().Be(recorder.ToSnapshot()[2].ContentBefore);
        var build = await new ProcessRunner(new SystemClock()).RunAsync(new("dotnet", ["build", "Fixture.slnx", "--no-restore", "--disable-build-servers"], fixture.Root, null), CancellationToken.None);
        build.ExitCode.Should().Be(0, build.StandardOutput + build.StandardError);
        var session = new ConversationSession("rename-session", "Rename", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Json, fixture.Root, fixture.Root, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "cp", null);
        await provider.GetRequiredService<ISessionStore>().SaveAsync(fixture.Root, session, CancellationToken.None);
        var coordinator = provider.GetRequiredService<CheckpointMutationCoordinator>();
        await coordinator.ApplyRecordedMutationsAsync(fixture.Root, session, "turn", "cp", recorder.ToSnapshot(), CancellationToken.None);
        (await coordinator.TryUndoAsync(fixture.Root, session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        foreach (var path in paths) File.ReadAllText(Path.Combine(fixture.Root, path)).Should().Be(original[path]);
        (await fixture.Service.ResolveSymbolAsync(new(fixture.Root), new("IInvoiceService"), null, CancellationToken.None)).Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        (await coordinator.TryRedoAsync(fixture.Root, session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        foreach (var path in paths) File.ReadAllText(Path.Combine(fixture.Root, path)).Should().Contain("IAccountService");
    }

    /// <summary>Read-only denies writes; workspace-write permits rename on an approved snapshot.</summary>
    [Fact]
    public async Task Permissions_and_conflicts_write_nothing_on_denial()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var provider = CreateProvider(fixture);
        var tools = provider.GetRequiredService<IToolExecutor>();
        var recorder = new TurnMutationAccumulator();
        var context = Context(fixture, recorder);
        (await tools.ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), context with { PermissionMode = PermissionMode.ReadOnly }, CancellationToken.None)).Result.Succeeded.Should().BeFalse();
        recorder.ToSnapshot().Should().BeEmpty();
        await fixture.Service.InspectSolutionAsync(new(fixture.Root), fixture.Authorization, CancellationToken.None);
        var conflict = await tools.ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "InvoiceService"), context, CancellationToken.None);
        conflict.Result.Succeeded.Should().BeFalse();
        File.ReadAllText(Path.Combine(fixture.Root, "Library/Invoice.cs")).Should().Contain("interface IInvoiceService");
        var success = await tools.ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), context with { PermissionMode = PermissionMode.WorkspaceWrite }, CancellationToken.None);
        success.Result.Succeeded.Should().BeTrue(success.Result.ErrorMessage);
    }

    /// <summary>Write failure rolls back; a rollback failure records the actual residual state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_write_rolls_back_or_records_residual_mutations(bool failRollback)
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var files = new FaultingFileSystem { BeforeWrite = (number, _, _) => { if (number == 2 || (failRollback && number == 3)) throw new IOException("Injected write failure"); } };
        using var provider = CreateProvider(fixture, files);
        var recorder = new TurnMutationAccumulator();
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), Context(fixture, recorder), CancellationToken.None);
        result.Result.Succeeded.Should().BeFalse();
        var payload = JsonSerializer.Deserialize(result.Result.StructuredOutputJson!, ProtocolJsonContext.Default.CSharpRenameResult)!;
        if (failRollback)
        {
            payload.ResidualPaths.Should().ContainSingle();
            recorder.ToSnapshot().Should().ContainSingle().Which.ContentAfter.Should().Be(File.ReadAllText(Path.Combine(fixture.Root, payload.ResidualPaths[0])));
        }
        else
        {
            payload.ResidualPaths.Should().BeEmpty();
            recorder.ToSnapshot().Should().BeEmpty();
            File.ReadAllText(Path.Combine(fixture.Root, "Consumer/InvoiceConsumer.cs")).Should().Contain("IInvoiceService");
        }
    }

    /// <summary>Cancellation uses a separate cleanup token and does not strand the first write.</summary>
    [Fact]
    public async Task Cancellation_rolls_back_completed_writes()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var files = new FaultingFileSystem { BeforeWrite = (number, _, _) => { if (number == 2) cancellation.Cancel(); } };
        using var provider = CreateProvider(fixture, files);
        var recorder = new TurnMutationAccumulator();
        var action = () => provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), Context(fixture, recorder), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        recorder.ToSnapshot().Should().BeEmpty();
        File.ReadAllText(Path.Combine(fixture.Root, "Consumer/InvoiceConsumer.cs")).Should().Contain("IInvoiceService");
    }


    /// <summary>Optimistic preconditions preserve concurrent external edits.</summary>
    [Fact]
    public async Task Concurrent_edits_are_preserved_before_first_write()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var externalPath = Path.Combine(fixture.Root, "Consumer/InvoiceConsumer.cs");
        var files = new FaultingFileSystem();
        files.BeforeRead = path => { if (path.EndsWith("InvoiceConsumer.cs", StringComparison.Ordinal)) { files.BeforeRead = null; File.AppendAllText(externalPath, "\n// external edit\n"); } };
        using var provider = CreateProvider(fixture, files);
        var recorder = new TurnMutationAccumulator();
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), Context(fixture, recorder), CancellationToken.None);
        result.Result.Succeeded.Should().BeFalse();
        recorder.ToSnapshot().Should().BeEmpty();
        File.ReadAllText(externalPath).Should().Contain("// external edit").And.Contain("IInvoiceService");
    }

    /// <summary>Ambiguous and invalid identifiers never create a rename plan.</summary>
    [Fact]
    public async Task Rename_rejects_ambiguity_and_generated_sources()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var ambiguous = await fixture.Service.PlanRenameAsync(request, new(Name: "Process", NewName: "Handle"), fixture.Authorization, CancellationToken.None);
        ambiguous.Changes.Should().BeEmpty();
        ambiguous.Resolution.Status.Should().Be(CSharpSymbolResolutionStatus.Ambiguous);
        var invalid = () => fixture.Service.PlanRenameAsync(request, new(Name: "IInvoiceService", NewName: "class"), null, CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentException>();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "Library/Generated.g.cs"), "namespace Billing; public class Generated {}");
        var generated = await fixture.Service.PlanRenameAsync(request, new(Name: "Generated", NewName: "Changed"), fixture.Authorization, CancellationToken.None);
        generated.Changes.Should().BeEmpty();
        generated.Errors.Should().Contain(error => error.Contains("Generated documents", StringComparison.Ordinal));
    }

    /// <summary>A deleted residual is persisted as a reversible deletion with its original bytes.</summary>
    [Fact]
    public async Task Deleted_residual_can_be_restored_by_undo()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var deleted = Path.Combine(fixture.Root, "Consumer", "InvoiceConsumer.cs");
        var original = await File.ReadAllTextAsync(deleted);
        var files = new FaultingFileSystem { BeforeWrite = (number, _, _) => { if (number == 2) { File.Delete(deleted); throw new IOException("Injected concurrent deletion"); } } };
        using var provider = CreateProvider(fixture, files);
        var recorder = new TurnMutationAccumulator();
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), Context(fixture, recorder), CancellationToken.None);
        result.Result.Succeeded.Should().BeFalse();
        var operation = recorder.ToSnapshot().Single();
        operation.Kind.Should().Be(FileMutationKind.Delete);
        operation.ContentAfter.Should().Be(original);
        var session = new ConversationSession("deleted-residual", "Rename", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Json, fixture.Root, fixture.Root, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "cp", null);
        await provider.GetRequiredService<ISessionStore>().SaveAsync(fixture.Root, session, CancellationToken.None);
        var coordinator = provider.GetRequiredService<CheckpointMutationCoordinator>();
        await coordinator.ApplyRecordedMutationsAsync(fixture.Root, session, "turn", "cp", recorder.ToSnapshot(), CancellationToken.None);
        (await coordinator.TryUndoAsync(fixture.Root, session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(deleted)).Should().Be(original);
    }

    /// <summary>References in an external linked source reject the whole rename before mutation.</summary>
    [Fact]
    public async Task Rename_rejects_changed_linked_document_outside_workspace()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var external = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cs");
        const string content = "public class ExternalConsumer { public Billing.IInvoiceService Service { get; set; } = null!; }";
        try
        {
            await File.WriteAllTextAsync(external, content);
            var project = Path.Combine(fixture.Root, "Consumer", "Consumer.csproj");
            var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", external), new XAttribute("Link", "External.cs")))); xml.Save(project);
            using var provider = CreateProvider(fixture);
            var recorder = new TurnMutationAccumulator();
            var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", Arguments("IInvoiceService", "IBillingService"), Context(fixture, recorder), CancellationToken.None);
            result.Result.Succeeded.Should().BeFalse();
            recorder.ToSnapshot().Should().BeEmpty();
            (await File.ReadAllTextAsync(external)).Should().Be(content);
            (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Library", "Invoice.cs"))).Should().Contain("interface IInvoiceService");
        }
        finally { File.Delete(external); }
    }

    private static ServiceProvider CreateProvider(DotNetFixtureWorkspace fixture, IFileSystem? files = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        if (files is not null) services.AddSingleton(files);
        return services.BuildServiceProvider();
    }
    private static ToolExecutionContext Context(DotNetFixtureWorkspace fixture, TurnMutationAccumulator recorder) => new("", "", fixture.Root, fixture.Root, PermissionMode.DangerFullAccess, OutputFormat.Json, null, IsInteractive: false, MutationRecorder: recorder);
    private static string Arguments(string name, string newName) => JsonSerializer.Serialize(new CSharpToolArguments(Name: name, Container: "Billing", NewName: newName), ProtocolJsonContext.Default.CSharpToolArguments);
}
