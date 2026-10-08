using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.IntegrationTests.Memory;

/// <summary>Exercises compiler read tools through their production registry and permissions.</summary>
public sealed class CSharpSemanticToolTests
{
    /// <summary>Cross-project references and hierarchy are compiler backed.</summary>
    [Fact]
    public async Task References_and_hierarchy_cross_project_boundaries()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        var arguments = new CSharpToolArguments(Name: "IInvoiceService", Container: "Billing");
        var references = await fixture.Service.FindReferencesAsync(request, arguments, fixture.Authorization, CancellationToken.None);
        references.Resolution.Status.Should().Be(CSharpSymbolResolutionStatus.Resolved);
        references.References.Should().Contain(reference => reference.ProjectPath == "Consumer/Consumer.csproj" && reference.Location.Path == "Consumer/InvoiceConsumer.cs");
        var hierarchy = await fixture.Service.GetTypeHierarchyAsync(request, arguments, null, CancellationToken.None);
        hierarchy.Implementations.Should().Contain(symbol => symbol.Name == "InvoiceService");
        var classes = await fixture.Service.GetTypeHierarchyAsync(request, new(Name: "InvoiceBase"), null, CancellationToken.None);
        classes.DerivedTypes.Should().Contain(symbol => symbol.Name == "InvoiceService");
        var page = await fixture.Service.FindReferencesAsync(request, arguments with { Limit = 1 }, null, CancellationToken.None);
        page.References.Should().ContainSingle();
        page.Truncated.Should().BeTrue();
        var ambiguous = await fixture.Service.FindReferencesAsync(request, new(Name: "Process"), null, CancellationToken.None);
        ambiguous.Resolution.Status.Should().Be(CSharpSymbolResolutionStatus.Ambiguous);
        ambiguous.References.Should().BeEmpty();
    }

    /// <summary>Fresh compiler diagnostics have code, location and filter support.</summary>
    [Fact]
    public async Task Diagnostics_refresh_source_and_filter_code_and_path()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var request = new DotNetWorkspaceRequest(fixture.Root);
        await fixture.Service.InspectSolutionAsync(request, fixture.Authorization, CancellationToken.None);
        var path = Path.Combine(fixture.Root, "Consumer", "InvoiceConsumer.cs");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("service.Create()", "service.Missing()", StringComparison.Ordinal));
        var result = await fixture.Service.GetDiagnosticsAsync(request, new(Path: "Consumer/InvoiceConsumer.cs", Severity: "error", Code: "CS1061"), null, CancellationToken.None);
        result.Diagnostics.Should().ContainSingle().Which.Location!.Line.Should().BeGreaterThan(0);
        result.Diagnostics[0].ProjectPath.Should().Be("Consumer/Consumer.csproj");
        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.CSharpDiagnosticsResult);
        JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.CSharpDiagnosticsResult).Should().BeEquivalentTo(result);
        json.Should().NotContain("Microsoft.CodeAnalysis");
    }

    /// <summary>Tool discovery preserves explicit schemas and cold versus cached permissions.</summary>
    [Fact]
    public async Task Registry_executor_enforces_cold_load_and_allows_cached_read_only_queries()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var services = new ServiceCollection();
        services.AddSharpClawTools();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();
        var definitions = await registry.ListAsync(fixture.Root, CancellationToken.None);
        var names = new[] { "dotnet_solution_inspect", "csharp_symbol_resolve", "csharp_find_references", "csharp_type_hierarchy", "csharp_diagnostics" };
        definitions.Where(definition => names.Contains(definition.Name)).Should().HaveCount(5).And.OnlyContain(definition => definition.InputSchemaJson != null);
        foreach (var definition in definitions.Where(definition => names.Contains(definition.Name)))
        {
            using var schema = JsonDocument.Parse(definition.InputSchemaJson!);
            schema.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        }
        var executor = provider.GetRequiredService<IToolExecutor>();
        var context = new ToolExecutionContext("", "", fixture.Root, fixture.Root, PermissionMode.ReadOnly, OutputFormat.Json, null, IsInteractive: false);
        var arguments = JsonSerializer.Serialize(new CSharpToolArguments(Name: "IInvoiceService"), ProtocolJsonContext.Default.CSharpToolArguments);
        var denied = await executor.ExecuteAsync("csharp_symbol_resolve", arguments, context, CancellationToken.None);
        denied.Result.Succeeded.Should().BeFalse();
        denied.Result.StructuredOutputJson.Should().Contain("PermissionDenied");
        var approved = await executor.ExecuteAsync("csharp_symbol_resolve", arguments, context with { PermissionMode = PermissionMode.DangerFullAccess }, CancellationToken.None);
        approved.Result.Succeeded.Should().BeTrue(approved.Result.ErrorMessage);
        var cached = await executor.ExecuteAsync("csharp_symbol_resolve", arguments, context, CancellationToken.None);
        cached.Result.Succeeded.Should().BeTrue(cached.Result.ErrorMessage);
        var restricted = await executor.ExecuteAsync("csharp_symbol_resolve", arguments, context with { AllowedTools = ["workspace_search"] }, CancellationToken.None);
        restricted.Result.Succeeded.Should().BeFalse();
    }
}
