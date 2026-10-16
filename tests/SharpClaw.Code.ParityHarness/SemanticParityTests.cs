using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Runtime.Turns;

namespace SharpClaw.Code.ParityHarness;

/// <summary>Offline compiler semantics through production tool execution.</summary>
public sealed class SemanticParityTests
{
    /// <summary>Resolves an exact interface through the standard tool boundary.</summary>
    [Fact]
    public async Task Semantic_symbol_resolution()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var services = new ServiceCollection();
        services.AddSharpClawTools();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        using var provider = services.BuildServiceProvider();
        var context = new ToolExecutionContext("", "", fixture.Root, fixture.Root, PermissionMode.DangerFullAccess, OutputFormat.Json, null, IsInteractive: false);
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_symbol_resolve", """{"name":"IInvoiceService","container":"Billing"}""", context, CancellationToken.None);
        result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        result.Result.StructuredOutputJson.Should().Contain("T:Billing.IInvoiceService");
    }

    /// <summary>Records every physical file changed by semantic rename.</summary>
    [Fact]
    public async Task Semantic_rename_mutation_recorded()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var services = new ServiceCollection();
        services.AddSharpClawTools();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        using var provider = services.BuildServiceProvider();
        var recorder = new TurnMutationAccumulator();
        var context = new ToolExecutionContext("", "", fixture.Root, fixture.Root, PermissionMode.DangerFullAccess, OutputFormat.Json, null, IsInteractive: false, MutationRecorder: recorder);
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("csharp_rename_symbol", """{"name":"IInvoiceService","newName":"IBillingService"}""", context, CancellationToken.None);
        result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        recorder.ToSnapshot().Should().HaveCount(2).And.OnlyContain(operation => operation.ContentBefore!.Contains("IInvoiceService") && operation.ContentAfter.Contains("IBillingService"));
    }
}
