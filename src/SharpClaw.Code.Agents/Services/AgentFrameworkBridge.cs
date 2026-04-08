using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Agents.Internal;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Agents.Services;

/// <summary>
/// Runs SharpClaw agents through Microsoft Agent Framework while hiding framework details from callers.
/// </summary>
public sealed class AgentFrameworkBridge(
    ProviderBackedAgentKernel providerBackedAgentKernel,
    ISystemClock systemClock,
    ILogger<AgentFrameworkBridge> logger) : IAgentFrameworkBridge
{
    /// <inheritdoc />
    public async Task<AgentRunResult> RunAsync(AgentFrameworkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProviderInvocationResult? providerResult = null;
        var frameworkAgent = new SharpClawFrameworkAgent(
            request.AgentId,
            request.Name,
            request.Description,
            async (messages, session, runOptions, ct) =>
            {
                providerResult = await providerBackedAgentKernel.ExecuteAsync(request, ct).ConfigureAwait(false);
                return new AgentResponse(new ChatMessage(ChatRole.Assistant, providerResult.Output));
            });

        var session = await frameworkAgent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        AgentResponse response;
        try
        {
            response = await frameworkAgent.RunAsync(request.Context.Prompt, session, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderExecutionException exception)
        {
            logger.LogError(
                exception,
                "Provider execution failed for {AgentId} with kind {FailureKind}.",
                request.AgentId,
                exception.Kind);
            throw;
        }

        var resolvedProviderResult = providerResult ?? new ProviderInvocationResult(
            Output: response.Text,
            Usage: new(request.Context.Prompt.Length, response.Text.Length, 0, request.Context.Prompt.Length + response.Text.Length, null),
            Summary: $"{request.Name} completed through the framework bridge.",
            ProviderRequest: null,
            ProviderEvents: null);

        logger.LogInformation("Completed framework-backed agent run for {AgentId}.", request.AgentId);
        var events = new RuntimeEvent[]
        {
            new AgentSpawnedEvent(
                EventId: $"event-{Guid.NewGuid():N}",
                SessionId: request.Context.SessionId,
                TurnId: request.Context.TurnId,
                OccurredAtUtc: systemClock.UtcNow,
                AgentId: request.AgentId,
                AgentKind: request.AgentKind,
                ParentAgentId: request.Context.ParentAgentId),
            new AgentCompletedEvent(
                EventId: $"event-{Guid.NewGuid():N}",
                SessionId: request.Context.SessionId,
                TurnId: request.Context.TurnId,
                OccurredAtUtc: systemClock.UtcNow,
                AgentId: request.AgentId,
                Succeeded: true,
                Summary: resolvedProviderResult.Summary,
                Usage: resolvedProviderResult.Usage)
        };

        return new AgentRunResult(
            AgentId: request.AgentId,
            AgentKind: request.AgentKind,
            Output: resolvedProviderResult.Output,
            Usage: resolvedProviderResult.Usage,
            Summary: resolvedProviderResult.Summary,
            ProviderRequest: resolvedProviderResult.ProviderRequest,
            ProviderEvents: resolvedProviderResult.ProviderEvents,
            ToolResults: [],
            Events: events);
    }
}
