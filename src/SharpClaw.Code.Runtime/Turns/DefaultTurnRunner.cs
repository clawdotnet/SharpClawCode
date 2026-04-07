using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>
/// Produces the initial agent-backed response for a prompt turn.
/// </summary>
public sealed class DefaultTurnRunner(
    IEnumerable<ISharpClawAgent> agents,
    PrimaryCodingAgent primaryCodingAgentFallback,
    IToolExecutor toolExecutor,
    IPromptContextAssembler promptContextAssembler) : ITurnRunner
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
        var mutationAccumulator = new TurnMutationAccumulator();

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
            DelegatedTask: request.DelegatedTask);

        var agentResult = await agent.RunAsync(agentContext, cancellationToken).ConfigureAwait(false);
        var mutations = mutationAccumulator.ToSnapshot();
        return new TurnRunResult(
            Output: agentResult.Output,
            Usage: agentResult.Usage,
            Summary: agentResult.Summary,
            ProviderRequest: agentResult.ProviderRequest,
            ProviderEvents: agentResult.ProviderEvents,
            ToolResults: agentResult.ToolResults,
            RuntimeEvents: agentResult.Events,
            FileMutations: mutations.Count == 0 ? null : mutations);
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
