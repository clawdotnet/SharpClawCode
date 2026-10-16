using System.Diagnostics;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Runtime.Verification;
using SharpClaw.Code.Telemetry.Diagnostics;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>
/// Produces the initial agent-backed response for a prompt turn.
/// </summary>
public sealed class DefaultTurnRunner(
    IEnumerable<ISharpClawAgent> agents,
    PrimaryCodingAgent primaryCodingAgentFallback,
    IToolExecutor toolExecutor,
    IPromptContextAssembler promptContextAssembler,
    IOptions<TelemetryOptions> telemetryOptions,
    IVerificationPolicyResolver? verificationPolicy = null,
    IVerificationLoopCoordinator? verificationLoop = null,
    TurnMutationJournal? journal = null) : ITurnRunner
{
    private readonly ISharpClawAgent[] agentList = agents.ToArray();

    /// <inheritdoc />
    public async Task<TurnRunResult> RunAsync(
        ConversationSession session,
        ConversationTurn turn,
        RunPromptRequest request,
        CancellationToken cancellationToken)
    {
        var promptContext = await promptContextAssembler
            .AssembleAsync(session, turn, request, cancellationToken)
            .ConfigureAwait(false);
        var requestedModel = promptContext.Metadata.TryGetValue("model", out var metadataModel)
            && !string.IsNullOrWhiteSpace(metadataModel)
            ? metadataModel
            : "default";
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? session.WorkingDirectory ?? "."
            : request.WorkingDirectory;

        var agent = ResolveAgent(request);
        var primaryMode = PrimaryModeResolver.ResolveEffective(request, session);
        var mutationAccumulator = journal?.Find(session.Id, turn.Id) ?? new TurnMutationAccumulator();
        var policy = verificationPolicy is null ? new VerificationPolicy() : await verificationPolicy.ResolveAsync(workingDirectory, request.Metadata, cancellationToken).ConfigureAwait(false);

        var agentContext = new AgentRunContext(
            SessionId: session.Id,
            TurnId: turn.Id,
            Prompt: promptContext.Prompt,
            WorkingDirectory: workingDirectory,
            Model: requestedModel,
            PermissionMode: request.PermissionMode,
            OutputFormat: request.OutputFormat,
            ToolExecutor: toolExecutor,
            Metadata: promptContext.Metadata,
            PrimaryMode: primaryMode,
            ToolMutationRecorder: mutationAccumulator,
            DelegatedTask: request.DelegatedTask,
            ConversationHistory: promptContext.ConversationHistory,
            IsInteractive: request.IsInteractive,
            ApprovalSettings: request.ApprovalSettings,
            UserContent: promptContext.UserContent);

        var telemetry = telemetryOptions.Value;
        using var turnScope = new TurnActivityScope(
            session.Id,
            turn.Id,
            telemetry.CapturePromptPreview ? promptContext.Prompt : null,
            telemetry.PromptPreviewMaxLength);
        var sw = Stopwatch.StartNew();
        AgentRunResult agentResult;
        try
        {
            agentResult = await agent.RunAsync(agentContext, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            turnScope.SetOutput(agentResult.Output, agentResult.Usage?.InputTokens, agentResult.Usage?.OutputTokens);
        }
        catch (Exception ex)
        {
            sw.Stop();
            turnScope.SetError(ex);
            throw;
        }

        var loop = verificationLoop is null ? new VerificationLoopResult(agentResult, null, [], [])
            : await verificationLoop.RunAsync(agent, agentContext, agentResult, mutationAccumulator, policy, cancellationToken).ConfigureAwait(false);
        agentResult = loop.AgentResult;
        var mutations = mutationAccumulator.ToSnapshot();
        return new TurnRunResult(
            Output: agentResult.Output,
            Usage: agentResult.Usage ?? new UsageSnapshot(0, 0, 0, 0, null),
            Summary: agentResult.Summary,
            ProviderRequest: agentResult.ProviderRequest,
            ProviderEvents: agentResult.ProviderEvents,
            ToolResults: agentResult.ToolResults,
            RuntimeEvents: agentResult.Events,
            FileMutations: mutations.Count == 0 ? null : mutations,
            Verification: loop.Verification, ProviderInvocations: loop.ProviderInvocations, PersistedRuntimeEvents: loop.PersistedEvents);
    }

    private ISharpClawAgent ResolveAgent(RunPromptRequest request)
    {
        var id = request.AgentId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return primaryCodingAgentFallback;
        }

        return agentList.FirstOrDefault(a => string.Equals(a.AgentId, id, StringComparison.OrdinalIgnoreCase))
            ?? primaryCodingAgentFallback;
    }
}
