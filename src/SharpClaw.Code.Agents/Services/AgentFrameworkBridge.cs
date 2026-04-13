using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Agents.Internal;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SharpClaw.Code.Agents.Services;

/// <summary>
/// Runs SharpClaw agents through Microsoft Agent Framework while hiding framework details from callers.
/// </summary>
public sealed class AgentFrameworkBridge(
    ProviderBackedAgentKernel providerBackedAgentKernel,
    IToolRegistry toolRegistry,
    ISystemClock systemClock,
    ILogger<AgentFrameworkBridge> logger) : IAgentFrameworkBridge
{
    /// <inheritdoc />
    public async Task<AgentRunResult> RunAsync(AgentFrameworkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowedTools = ResolveAllowedTools(request.Context.Metadata);

        // Build tool execution context from agent run context
        var toolExecutionContext = new ToolExecutionContext(
            SessionId: request.Context.SessionId,
            TurnId: request.Context.TurnId,
            WorkspaceRoot: request.Context.WorkingDirectory,
            WorkingDirectory: request.Context.WorkingDirectory,
            PermissionMode: request.Context.PermissionMode,
            OutputFormat: request.Context.OutputFormat,
            EnvironmentVariables: null,
            AllowedTools: allowedTools,
            AllowDangerousBypass: false,
            IsInteractive: false,
            SourceKind: PermissionRequestSourceKind.Runtime,
            SourceName: null,
            TrustedPluginNames: null,
            TrustedMcpServerNames: null,
            PrimaryMode: request.Context.PrimaryMode,
            MutationRecorder: request.Context.ToolMutationRecorder);

        // Map tool definitions from the registry to provider tool definitions
        var registryTools = await toolRegistry.ListAsync(
            request.Context.WorkingDirectory,
            cancellationToken).ConfigureAwait(false);

        var providerTools = registryTools
            .Select(t => new ProviderToolDefinition(t.Name, t.Description, t.InputSchemaJson))
            .ToList();

        ProviderInvocationResult? providerResult = null;
        var frameworkAgent = new SharpClawFrameworkAgent(
            request.AgentId,
            request.Name,
            request.Description,
            async (messages, session, runOptions, ct) =>
            {
                providerResult = await providerBackedAgentKernel.ExecuteAsync(
                    request,
                    toolExecutionContext,
                    providerTools,
                    ct).ConfigureAwait(false);
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

        var events = new List<RuntimeEvent>
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

        // Include tool-related events from the kernel
        if (resolvedProviderResult.ToolEvents is { Count: > 0 } toolEvents)
        {
            events.AddRange(toolEvents);
        }

        return new AgentRunResult(
            AgentId: request.AgentId,
            AgentKind: request.AgentKind,
            Output: resolvedProviderResult.Output,
            Usage: resolvedProviderResult.Usage,
            Summary: resolvedProviderResult.Summary,
            ProviderRequest: resolvedProviderResult.ProviderRequest,
            ProviderEvents: resolvedProviderResult.ProviderEvents,
            ToolResults: resolvedProviderResult.ToolResults ?? [],
            Events: events);
    }

    private static IReadOnlyCollection<string>? ResolveAllowedTools(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null
            || !metadata.TryGetValue(SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.StringArray);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
