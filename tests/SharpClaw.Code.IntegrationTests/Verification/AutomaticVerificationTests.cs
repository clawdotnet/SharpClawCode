using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Runtime.Verification;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.IntegrationTests.Verification;

/// <summary>Exercises logical turn accounting, exact repair budgets and durable mutation finalization.</summary>
public sealed class AutomaticVerificationTests
{
    /// <summary>Failed verification performs only the permitted repair attempts and records one reversible logical turn.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task Repair_budget_is_exact_and_failed_turn_is_truthful_and_reversible(int budget, int expectedCalls)
    {
        using var fixture = new Fixture();
        var agent = new RecordingAgent();
        var verification = new FixedVerification();
        using var provider = Provider(agent, verification);
        var runtime = provider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(Request(fixture.Root, budget), CancellationToken.None);
        agent.Calls.Should().Be(expectedCalls);
        verification.Calls.Should().Be(expectedCalls);
        result.Verification!.Status.Should().Be(VerificationStatus.Failed);
        result.Verification.RepairAttempts.Should().Be(budget);
        result.Session.State.Should().Be(SessionLifecycleState.Failed);
        result.Usage!.TotalTokens.Should().Be(5 * expectedCalls);
        result.Events.OfType<TurnCompletedEvent>().Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        result.Events.OfType<ProviderStartedEvent>().Should().HaveCount(expectedCalls);
        result.Events.OfType<VerificationStartedEvent>().Should().HaveCount(expectedCalls);
        result.Events.OfType<VerificationRepairStartedEvent>().Should().HaveCount(budget);
        var durable = await provider.GetRequiredService<IEventStore>().ReadAllAsync(fixture.Root, result.Session.Id, CancellationToken.None);
        durable.OfType<VerificationCompletedEvent>().Should().HaveCount(expectedCalls);
        durable.OfType<TurnCompletedEvent>().Single().Turn.Verification.Should().BeEquivalentTo(result.Verification);
        var coordinator = provider.GetRequiredService<CheckpointMutationCoordinator>();
        (await coordinator.TryUndoAsync(fixture.Root, result.Session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(fixture.Root, "Source.cs")).Should().Be("original");
        (await coordinator.TryRedoAsync(fixture.Root, result.Session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(fixture.Root, "Source.cs")).Should().Be("state" + expectedCalls);
        if (budget > 0) agent.Contexts[1].Prompt.Should().Contain("CS1002").And.Contain("Repair attempt 1 of 2");
    }

    /// <summary>Successful repair stops immediately; disabled behavior and permission denial never start repair.</summary>
    [Fact]
    public async Task Successful_repair_disabled_path_and_permission_denial_are_distinct()
    {
        using var fixture = new Fixture();
        var agent = new RecordingAgent();
        var verification = new FixedVerification { PassAt = 2 };
        using var provider = Provider(agent, verification);
        var runtime = provider.GetRequiredService<IConversationRuntime>();
        var repaired = await runtime.RunPromptAsync(Request(fixture.Root, 2), CancellationToken.None);
        repaired.Verification!.Status.Should().Be(VerificationStatus.Passed);
        agent.Calls.Should().Be(2);
        repaired.Events.OfType<TurnCompletedEvent>().Single().Succeeded.Should().BeTrue();
        var disabled = Request(fixture.Root, 2) with { Metadata = new() { [SharpClawWorkflowMetadataKeys.VerificationEnabled] = "false" } };
        (await runtime.RunPromptAsync(disabled, CancellationToken.None)).Verification.Should().BeNull();
        verification.Calls.Should().Be(2);
        var denied = await runtime.RunPromptAsync(Request(fixture.Root, 2) with { PermissionMode = PermissionMode.WorkspaceWrite }, CancellationToken.None);
        denied.Verification!.Reason.Should().Be(VerificationFailureReason.PermissionDenied);
        agent.Calls.Should().Be(4);
        verification.Calls.Should().Be(2);
    }

    /// <summary>Edits remain durably recoverable when the initial agent throws or execution is cancelled.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_agent_preserves_mutations(bool cancelled)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var agent = new RecordingAgent { AfterWrite = () => { if (cancelled) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); } throw new InvalidOperationException("agent failed after editing"); } };
        using var provider = Provider(agent, new FixedVerification());
        var runtime = provider.GetRequiredService<IConversationRuntime>();
        var action = () => runtime.RunPromptAsync(Request(fixture.Root, 2), cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        var session = await provider.GetRequiredService<ISessionStore>().GetLatestAsync(fixture.Root, CancellationToken.None);
        session!.State.Should().Be(SessionLifecycleState.Failed);
        session.LastCheckpointId.Should().NotBeNull();
        var set = await provider.GetRequiredService<IMutationSetStore>().GetAsync(fixture.Root, session.Id, session.LastCheckpointId!, CancellationToken.None);
        set!.Operations.Should().ContainSingle();
        (await provider.GetRequiredService<CheckpointMutationCoordinator>().TryUndoAsync(fixture.Root, session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(fixture.Root, "Source.cs")).Should().Be("original");
    }

    /// <summary>A same-turn restart cannot reset a consumed repair budget.</summary>
    [Fact]
    public async Task Durable_attempt_events_prevent_budget_reset()
    {
        using var fixture = new Fixture();
        var agent = new RecordingAgent();
        var verifier = new FixedVerification();
        using var provider = Provider(agent, verifier);
        var runtime = provider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(Request(fixture.Root, 2), CancellationToken.None);
        var recorder = new TurnMutationAccumulator();
        recorder.Record(new("op", FileMutationKind.Replace, "write_file", "Source.cs", "original", "state3"));
        var context = agent.Contexts[0];
        var loop = await provider.GetRequiredService<IVerificationLoopCoordinator>().RunAsync(agent, context, RecordingAgent.Result(context), recorder, new(true, MaxRepairIterations: 5), CancellationToken.None);
        loop.Verification!.RepairAttempts.Should().Be(2);
        agent.Calls.Should().Be(3);
        verifier.Calls.Should().Be(3);
        loop.Verification.Status.Should().Be(VerificationStatus.Failed);
        (await provider.GetRequiredService<IVerificationLoopCoordinator>().RunAsync(agent, context with { PrimaryMode = PrimaryMode.Plan }, RecordingAgent.Result(context), recorder, new(true), CancellationToken.None)).Verification.Should().BeNull();
        (await provider.GetRequiredService<IVerificationLoopCoordinator>().RunAsync(agent, context, RecordingAgent.Result(context), new(), new(true), CancellationToken.None)).Verification.Should().BeNull();
    }


    /// <summary>A failed repair retains its edits in the logical turn's checkpoint.</summary>
    [Fact]
    public async Task Failed_repair_agent_preserves_all_edits()
    {
        using var fixture = new Fixture();
        var agent = new RecordingAgent();
        agent.AfterWrite = () => { if (agent.Calls == 2) throw new InvalidOperationException("repair failed after editing"); };
        using var provider = Provider(agent, new FixedVerification());
        var result = await provider.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request(fixture.Root, 2), CancellationToken.None);
        result.Verification!.Reason.Should().Be(VerificationFailureReason.ProcessFailed);
        result.Events.OfType<VerificationRepairCompletedEvent>().Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        var set = await provider.GetRequiredService<IMutationSetStore>().GetAsync(fixture.Root, result.Session.Id, result.Checkpoint!.Id, CancellationToken.None);
        set!.Operations.Should().HaveCount(2);
        (await provider.GetRequiredService<CheckpointMutationCoordinator>().TryUndoAsync(fixture.Root, result.Session.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(fixture.Root, "Source.cs")).Should().Be("original");
    }

    private static RunPromptRequest Request(string root, int budget) => new("make the change", null, root, PermissionMode.DangerFullAccess, OutputFormat.Json,
        new() { [SharpClawWorkflowMetadataKeys.VerificationEnabled] = "true", [SharpClawWorkflowMetadataKeys.VerificationMaxRepairIterations] = budget.ToString() }, AgentId: "fixture-agent", IsInteractive: false);
    private static ServiceProvider Provider(RecordingAgent agent, FixedVerification verification)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<ISharpClawAgent>(agent);
        services.AddSingleton<IVerificationService>(verification);
        services.AddSingleton<IPromptContextAssembler, SimplePrompt>();
        return services.BuildServiceProvider();
    }
    private sealed class SimplePrompt : IPromptContextAssembler
    {
        public Task<PromptExecutionContext> AssembleAsync(ConversationSession session, ConversationTurn turn, RunPromptRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PromptExecutionContext(request.Prompt, request.Metadata ?? new()));
    }
    private sealed class RecordingAgent : ISharpClawAgent
    {
        public string AgentId => "fixture-agent";
        public string AgentKind => "fixture";
        internal int Calls { get; private set; }
        internal List<AgentRunContext> Contexts { get; } = [];
        internal Action? AfterWrite { get; set; }
        public async Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Contexts.Add(context);
            var toolContext = new ToolExecutionContext(context.SessionId, context.TurnId, context.WorkingDirectory, context.WorkingDirectory, context.PermissionMode, context.OutputFormat, null, IsInteractive: context.IsInteractive, MutationRecorder: context.ToolMutationRecorder);
            var result = await context.ToolExecutor.ExecuteAsync("write_file", JsonSerializer.Serialize(new { path = "Source.cs", content = "state" + Calls }), toolContext, cancellationToken);
            result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
            AfterWrite?.Invoke();
            return Result(context) with { ToolResults = [result.Result] };
        }
        internal static AgentRunResult Result(AgentRunContext context)
        {
            var request = new ProviderRequest("request-" + Guid.NewGuid(), context.SessionId, context.TurnId, "fixture", "fixture-model", context.Prompt, null, context.OutputFormat, null, null);
            return new("fixture-agent", "fixture", "agent completed", new(2, 3, 0, 5, null), "completed", request, [new("event-" + Guid.NewGuid(), request.Id, "completed", DateTimeOffset.UtcNow, "done", true, new(2, 3, 0, 5, null))]);
        }
    }
    private sealed class FixedVerification : IVerificationService
    {
        internal int Calls { get; private set; }
        internal int PassAt { get; set; } = int.MaxValue;
        public Task<VerificationRunReport> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var status = Calls >= PassAt ? VerificationStatus.Passed : VerificationStatus.Failed;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new VerificationRunReport(request.WorkspaceRoot, "Fixture.csproj", request.Scope, status, status == VerificationStatus.Passed ? VerificationFailureReason.None : VerificationFailureReason.BuildFailed, now, now, 0, ["Fixture.csproj"], new(status, [], [new("Source.cs", 1, 1, "CS1002", "error", "; expected", "Fixture.csproj")]), null, null, "fixture check"));
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpclaw-auto-" + Guid.NewGuid().ToString("N"));
        internal Fixture() { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, "Source.cs"), "original"); Directory.CreateDirectory(Path.Combine(Root, ".sharpclaw")); File.WriteAllText(Path.Combine(Root, ".sharpclaw/config.jsonc"), """{"shareMode":"manual","verification":{"enabled":false}}"""); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
