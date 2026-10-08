using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.IntegrationTests.Verification;
/// <summary>Real SDK build/test repair acceptance without external model credentials.</summary>
public sealed class RealAutomaticVerificationTests
{
    /// <summary>A test-sensitive mutation is repaired and reverified within the same logical turn.</summary>
    [Fact]
    public async Task Test_sensitive_edit_is_repaired_and_reverified()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        var agent = new RepairAgent();
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        services.AddSingleton<ISharpClawAgent>(agent);
        services.AddSingleton<IPromptContextAssembler, Prompt>();
        using var provider = services.BuildServiceProvider();
        var request = new RunPromptRequest("Change the invoice implementation, keeping the test passing.", null, fixture.Root, PermissionMode.DangerFullAccess, OutputFormat.Json,
            new() { [SharpClawWorkflowMetadataKeys.VerificationEnabled] = "true" }, AgentId: agent.AgentId, IsInteractive: false);
        var result = await provider.GetRequiredService<IConversationRuntime>().RunPromptAsync(request, CancellationToken.None);
        result.Verification!.Status.Should().Be(VerificationStatus.Passed, result.Verification.Summary);
        result.Verification.RepairAttempts.Should().Be(1);
        result.Verification.Tests!.Passed.Should().Be(1);
        agent.Calls.Should().Be(2);
        agent.RepairPrompt.Should().Contain("Invoice_has_expected_value");
        result.ToolResults.Should().HaveCount(4);
    }
    private sealed class Prompt : IPromptContextAssembler
    {
        public Task<PromptExecutionContext> AssembleAsync(ConversationSession session, ConversationTurn turn, RunPromptRequest request, CancellationToken cancellationToken) => Task.FromResult(new PromptExecutionContext(request.Prompt, request.Metadata ?? new()));
    }
    private sealed class RepairAgent : ISharpClawAgent
    {
        public string AgentId => "test-repair-agent";
        public string AgentKind => "fixture";
        internal int Calls { get; private set; }
        internal string? RepairPrompt { get; private set; }
        public async Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 2) RepairPrompt = context.Prompt;
            var toolContext = new ToolExecutionContext(context.SessionId, context.TurnId, context.WorkingDirectory, context.WorkingDirectory, context.PermissionMode, OutputFormat.Json, null, IsInteractive: false, MutationRecorder: context.ToolMutationRecorder);
            var result = await context.ToolExecutor.ExecuteAsync("edit_file", JsonSerializer.Serialize(new { path = "Library/Invoice.cs", oldString = Calls == 1 ? "\"invoice\"" : "\"wrong\"", newString = Calls == 1 ? "\"wrong\"" : "\"invoice\"" }), toolContext, cancellationToken);
            result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
            return new(AgentId, AgentKind, "Updated invoice", new(1, 1, 0, 2, null), "Updated invoice", ToolResults: [result.Result]);
        }
    }
}
