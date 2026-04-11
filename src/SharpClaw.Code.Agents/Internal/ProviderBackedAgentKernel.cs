using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Agents.Configuration;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Internal;

/// <summary>
/// Executes the provider-backed core of a SharpClaw agent run,
/// including a multi-iteration tool-calling loop.
/// </summary>
public sealed class ProviderBackedAgentKernel(
    IProviderRequestPreflight providerRequestPreflight,
    IModelProviderResolver providerResolver,
    IAuthFlowService authFlowService,
    ToolCallDispatcher toolCallDispatcher,
    IOptions<AgentLoopOptions> loopOptions,
    ILogger<ProviderBackedAgentKernel> logger)
{
    internal async Task<ProviderInvocationResult> ExecuteAsync(
        AgentFrameworkRequest request,
        ToolExecutionContext? toolExecutionContext,
        IReadOnlyList<ProviderToolDefinition>? availableTools,
        CancellationToken cancellationToken)
    {
        var options = loopOptions.Value;
        var requestedModel = request.Context.Model;
        var requestedProvider = request.Context.Metadata is not null && request.Context.Metadata.TryGetValue("provider", out var metadataProvider)
            ? metadataProvider
            : string.Empty;

        var baseMetadata = request.Context.Metadata is null
            ? null
            : new Dictionary<string, string>(request.Context.Metadata, StringComparer.Ordinal);

        // Run a single preflight to resolve the effective provider name for auth/resolution
        var resolvedRequest = providerRequestPreflight.Prepare(new ProviderRequest(
            Id: $"provider-request-{Guid.NewGuid():N}",
            SessionId: request.Context.SessionId,
            TurnId: request.Context.TurnId,
            ProviderName: requestedProvider ?? string.Empty,
            Model: requestedModel,
            Prompt: request.Context.Prompt,
            SystemPrompt: request.Instructions,
            OutputFormat: request.Context.OutputFormat,
            Temperature: 0.1m,
            Metadata: baseMetadata));

        var resolvedProviderName = resolvedRequest.ProviderName;

        try
        {
            // --- Auth check ---
            AuthStatus authStatus;
            try
            {
                authStatus = await authFlowService.GetStatusAsync(resolvedProviderName, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                throw CreateMissingProviderException(resolvedProviderName, requestedModel, "auth status lookup");
            }
            catch (Exception exception)
            {
                throw new ProviderExecutionException(
                    resolvedProviderName,
                    requestedModel,
                    ProviderFailureKind.AuthenticationUnavailable,
                    $"Provider '{resolvedProviderName}' authentication probe failed.",
                    exception);
            }

            if (!authStatus.IsAuthenticated)
            {
                logger.LogWarning(
                    "Provider {ProviderName} is not authenticated for session {SessionId}.",
                    resolvedProviderName,
                    request.Context.SessionId);
                throw new ProviderExecutionException(
                    resolvedProviderName,
                    requestedModel,
                    ProviderFailureKind.AuthenticationUnavailable,
                    $"Provider '{resolvedProviderName}' is not authenticated.");
            }

            // --- Resolve provider ---
            IModelProvider provider;
            try
            {
                provider = providerResolver.Resolve(resolvedProviderName);
            }
            catch (InvalidOperationException)
            {
                throw CreateMissingProviderException(resolvedProviderName, requestedModel, "provider resolution");
            }

            // --- Build initial conversation messages ---
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(request.Instructions))
            {
                messages.Add(new ChatMessage("system", [new ContentBlock(ContentBlockKind.Text, request.Instructions, null, null, null, null)]));
            }

            messages.Add(new ChatMessage("user", [new ContentBlock(ContentBlockKind.Text, request.Context.Prompt, null, null, null, null)]));

            // --- Tool-calling loop ---
            var allProviderEvents = new List<ProviderEvent>();
            var allToolResults = new List<ToolResult>();
            var allToolEvents = new List<RuntimeEvent>();
            var outputSegments = new List<string>();
            UsageSnapshot? terminalUsage = null;
            ProviderRequest? lastProviderRequest = null;

            for (var iteration = 0; iteration < options.MaxToolIterations; iteration++)
            {
                var providerRequest = providerRequestPreflight.Prepare(new ProviderRequest(
                    Id: $"provider-request-{Guid.NewGuid():N}",
                    SessionId: request.Context.SessionId,
                    TurnId: request.Context.TurnId,
                    ProviderName: resolvedProviderName,
                    Model: requestedModel,
                    Prompt: request.Context.Prompt,
                    SystemPrompt: request.Instructions,
                    OutputFormat: request.Context.OutputFormat,
                    Temperature: 0.1m,
                    Metadata: baseMetadata,
                    Messages: messages,
                    Tools: availableTools,
                    MaxTokens: options.MaxTokensPerRequest));

                lastProviderRequest = providerRequest;

                var stream = await provider.StartStreamAsync(providerRequest, cancellationToken).ConfigureAwait(false);
                var iterationTextSegments = new List<string>();
                var toolUseEvents = new List<ProviderEvent>();

                await foreach (var providerEvent in stream.Events.WithCancellation(cancellationToken))
                {
                    allProviderEvents.Add(providerEvent);

                    if (!providerEvent.IsTerminal && !string.IsNullOrWhiteSpace(providerEvent.Content))
                    {
                        iterationTextSegments.Add(providerEvent.Content);
                    }

                    if (!string.IsNullOrEmpty(providerEvent.ToolUseId) && !string.IsNullOrEmpty(providerEvent.ToolName))
                    {
                        toolUseEvents.Add(providerEvent);
                    }

                    if (providerEvent.IsTerminal && providerEvent.Usage is not null)
                    {
                        terminalUsage = providerEvent.Usage;
                    }
                }

                // If no tool-use events, accumulate text and break
                if (toolUseEvents.Count == 0)
                {
                    outputSegments.AddRange(iterationTextSegments);
                    break;
                }

                // Build assistant message with text + tool-use content blocks
                var assistantBlocks = new List<ContentBlock>();
                var iterationText = string.Concat(iterationTextSegments);
                if (!string.IsNullOrEmpty(iterationText))
                {
                    assistantBlocks.Add(new ContentBlock(ContentBlockKind.Text, iterationText, null, null, null, null));
                }

                foreach (var toolUseEvent in toolUseEvents)
                {
                    assistantBlocks.Add(new ContentBlock(
                        ContentBlockKind.ToolUse,
                        null,
                        toolUseEvent.ToolUseId,
                        toolUseEvent.ToolName,
                        toolUseEvent.ToolInputJson,
                        null));
                }

                messages.Add(new ChatMessage("assistant", assistantBlocks));

                // Dispatch each tool call and collect results
                var toolResultBlocks = new List<ContentBlock>();
                foreach (var toolUseEvent in toolUseEvents)
                {
                    if (toolExecutionContext is null)
                    {
                        // No tool execution context means we cannot dispatch tools
                        toolResultBlocks.Add(new ContentBlock(
                            ContentBlockKind.ToolResult,
                            "Tool execution is not available in this context.",
                            toolUseEvent.ToolUseId,
                            null,
                            null,
                            true));
                        continue;
                    }

                    var (resultBlock, toolResult, events) = await toolCallDispatcher.DispatchAsync(
                        toolUseEvent,
                        toolExecutionContext,
                        cancellationToken).ConfigureAwait(false);

                    toolResultBlocks.Add(resultBlock);
                    allToolResults.Add(toolResult);
                    allToolEvents.AddRange(events);
                }

                messages.Add(new ChatMessage("user", toolResultBlocks));

                // Accumulate partial text from tool-calling iterations
                if (!string.IsNullOrEmpty(iterationText))
                {
                    outputSegments.Add(iterationText);
                }
            }

            var output = string.Concat(outputSegments);
            if (string.IsNullOrWhiteSpace(output))
            {
                logger.LogWarning(
                    "Provider {ProviderName} returned no stream content for session {SessionId}; returning placeholder response.",
                    resolvedProviderName,
                    request.Context.SessionId);
                return CreatePlaceholderResult(request, requestedModel, $"Provider '{resolvedProviderName}' returned no content; using placeholder response.");
            }

            var usage = terminalUsage ?? new UsageSnapshot(
                InputTokens: request.Context.Prompt.Length,
                OutputTokens: output.Length,
                CachedInputTokens: 0,
                TotalTokens: request.Context.Prompt.Length + output.Length,
                EstimatedCostUsd: null);

            return new ProviderInvocationResult(
                Output: output,
                Usage: usage,
                Summary: $"Streamed provider response from {resolvedProviderName}/{requestedModel}.",
                ProviderRequest: lastProviderRequest,
                ProviderEvents: allProviderEvents,
                ToolResults: allToolResults.Count > 0 ? allToolResults : null,
                ToolEvents: allToolEvents.Count > 0 ? allToolEvents : null);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Provider execution was canceled for session {SessionId}, turn {TurnId}.",
                request.Context.SessionId,
                request.Context.TurnId);
            throw;
        }
        catch (ProviderExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProviderExecutionException(
                resolvedProviderName,
                requestedModel,
                ProviderFailureKind.StreamFailed,
                $"Provider '{resolvedProviderName}' failed during execution.",
                exception);
        }
    }

    /// <summary>
    /// Backward-compatible overload for callers that do not need tool calling.
    /// </summary>
    internal Task<ProviderInvocationResult> ExecuteAsync(AgentFrameworkRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(request, toolExecutionContext: null, availableTools: null, cancellationToken);

    private static ProviderInvocationResult CreatePlaceholderResult(AgentFrameworkRequest request, string model, string summary)
    {
        var output = $"Session {request.Context.SessionId} turn {request.Context.TurnId}: placeholder response for '{request.Context.Prompt}' using model '{model}'.";
        var usage = new UsageSnapshot(
            InputTokens: request.Context.Prompt.Length,
            OutputTokens: output.Length,
            CachedInputTokens: 0,
            TotalTokens: request.Context.Prompt.Length + output.Length,
            EstimatedCostUsd: 0.0001m);

        return new ProviderInvocationResult(output, usage, summary, null, null);
    }

    private static ProviderExecutionException CreateMissingProviderException(
        string providerName,
        string model,
        string stage)
        => new(
            providerName,
            model,
            ProviderFailureKind.MissingProvider,
            $"No provider named '{providerName}' was registered during {stage}.");
}
