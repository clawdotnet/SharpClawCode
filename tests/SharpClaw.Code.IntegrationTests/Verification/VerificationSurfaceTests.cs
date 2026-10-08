using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpClaw.Code.Cli.Composition;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.IntegrationTests.Verification;

/// <summary>Checks the common CLI, REPL, agent permission, and prompt diagnostics surfaces.</summary>
public sealed class VerificationSurfaceTests
{
    /// <summary>Read-only and noninteractive calls deny execution; explicit scoped approval permits it.</summary>
    [Fact]
    public async Task Tool_permissions_precede_all_process_work_and_diagnostics_never_execute()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var runner = new RecordingRunner();
        using var provider = Provider(fixture, runner);
        var tools = provider.GetRequiredService<IToolExecutor>();
        var context = new ToolExecutionContext("", "", fixture.Root, fixture.Root, PermissionMode.ReadOnly, OutputFormat.Json, null, IsInteractive: false);
        var denied = await tools.ExecuteAsync("verify_workspace", """{"scope":"build"}""", context, CancellationToken.None);
        JsonSerializer.Deserialize(denied.Result.StructuredOutputJson!, ProtocolJsonContext.Default.VerificationRunReport)!.Reason.Should().Be(VerificationFailureReason.PermissionDenied);
        (await tools.ExecuteAsync("verify_workspace", "{}", context with { PermissionMode = PermissionMode.WorkspaceWrite }, CancellationToken.None)).Result.Succeeded.Should().BeFalse();
        await provider.GetRequiredService<IWorkspaceDiagnosticsService>().BuildSnapshotAsync(fixture.Root, CancellationToken.None);
        runner.Requests.Should().BeEmpty();
        var settings = new ApprovalSettings([ApprovalScope.ShellExecution], 1);
        var allowed = await tools.ExecuteAsync("verify_workspace", """{"scope":"build"}""", context with { PermissionMode = PermissionMode.WorkspaceWrite, ApprovalSettings = settings }, CancellationToken.None);
        allowed.Result.Succeeded.Should().BeTrue(allowed.Result.ErrorMessage);
        runner.Requests.Should().ContainSingle();
        (await tools.ExecuteAsync("verify_workspace", """{"scope":"build"}""", context with { PermissionMode = PermissionMode.WorkspaceWrite, ApprovalSettings = settings }, CancellationToken.None)).Result.Succeeded.Should().BeFalse();
    }

    /// <summary>CLI and slash commands bind identical options and expose typed failed reports.</summary>
    [Fact]
    public async Task Cli_and_slash_verify_bind_shared_options_and_render_failures()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        var capture = new Capture();
        using var provider = Provider(fixture, capture: capture);
        var root = await provider.GetRequiredService<CliCommandFactory>().CreateRootCommandAsync();
        var path = Path.Combine(fixture.Root, "Library/Invoice.cs");
        await File.AppendAllTextAsync(path, "\ninvalid C# here;\n");
        var exit = await root.Parse(["--cwd", fixture.Root, "--permission-mode", "dangerFullAccess", "--output-format", "json", "verify", "--scope", "build", "--target", "Fixture.slnx"]).InvokeAsync();
        exit.Should().Be(1);
        var report = JsonSerializer.Deserialize(capture.Results[^1].DataJson!, ProtocolJsonContext.Default.VerificationRunReport)!;
        report.Reason.Should().Be(VerificationFailureReason.BuildFailed);
        report.Build!.Diagnostics.Should().Contain(item => item.Code.StartsWith("CS", StringComparison.Ordinal) && item.Path == "Library/Invoice.cs");
        var context = new CommandExecutionContext(fixture.Root, null, PermissionMode.DangerFullAccess, OutputFormat.Json, PrimaryMode.Build);
        var slash = provider.GetServices<ISlashCommandHandler>().Single(handler => handler.CommandName == "verify");
        (await slash.ExecuteAsync(new(true, "verify", ["--scope", "build", "--target", "Fixture.slnx"]), context, CancellationToken.None)).Should().Be(1);
        var state = provider.GetRequiredService<ReplInteractionState>();
        state.PermissionModeOverride = PermissionMode.ReadOnly;
        (await slash.ExecuteAsync(new(true, "verify", []), context, CancellationToken.None)).Should().Be(1);
        capture.Results[^1].DataJson.Should().Contain("PermissionDenied");
        (await slash.ExecuteAsync(new(true, "verify", ["--scope", "invalid"]), context, CancellationToken.None)).Should().NotBe(0);
    }

    private static ServiceProvider Provider(DotNetFixtureWorkspace fixture, IProcessRunner? runner = null, Capture? capture = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSharpClawCli();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        if (runner is not null) services.AddSingleton(runner);
        if (capture is not null)
        {
            services.RemoveAll<IOutputRenderer>();
            services.AddSingleton<IOutputRenderer>(capture);
        }
        return services.BuildServiceProvider();
    }
    private sealed class RecordingRunner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }
    private sealed class Capture : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Json;
        internal List<CommandResult> Results { get; } = [];
        public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken) { Results.Add(result); return Task.CompletedTask; }
        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
