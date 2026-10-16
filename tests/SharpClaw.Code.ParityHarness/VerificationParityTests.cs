using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.ParityHarness;
/// <summary>Deterministic verification process outcomes through real permissions and tools.</summary>
public sealed class VerificationParityTests
{
    /// <summary>Build success is structured.</summary>
    [Fact]
    public Task Verification_build_success() => RunAsync(0, PermissionMode.DangerFullAccess, true);
    /// <summary>Nonzero build exit is failure even with no diagnostics.</summary>
    [Fact]
    public Task Verification_build_failure() => RunAsync(1, PermissionMode.DangerFullAccess, false);
    /// <summary>Read-only blocks verification before process execution.</summary>
    [Fact]
    public Task Verification_permission_denied() => RunAsync(0, PermissionMode.ReadOnly, false);
    private static async Task RunAsync(int exit, PermissionMode mode, bool expected)
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var runner = new FixedRunner(exit);
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        services.AddSingleton<IProcessRunner>(runner);
        using var provider = services.BuildServiceProvider();
        var context = new ToolExecutionContext("", "", fixture.Root, fixture.Root, mode, OutputFormat.Json, null, IsInteractive: false);
        var result = await provider.GetRequiredService<IToolExecutor>().ExecuteAsync("verify_workspace", """{"scope":"build"}""", context, CancellationToken.None);
        result.Result.Succeeded.Should().Be(expected);
        result.Result.StructuredOutputJson.Should().NotBeNull();
        runner.Calls.Should().Be(mode == PermissionMode.ReadOnly ? 0 : 1);
    }
    private sealed class FixedRunner(int exit) : IProcessRunner
    {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new ProcessRunResult(exit, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)); }
    }
}
