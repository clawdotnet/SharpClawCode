using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Agents.Configuration;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Telemetry.Diagnostics;
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
    IOptions<ProviderCatalogOptions> providerCatalogOptions,
    ISystemClock systemClock,
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
            Metadata: baseMetadata,
            ContainsImageInput: request.Context.UserContent?.Any(static block => block.Kind == ContentBlockKind.Image) == true));

        requestedModel = resolvedRequest.Model;
        var resolvedProviderName = resolvedRequest.ProviderName;

        try
        {
            // Resolve and authenticate the primary provider plus configured fallbacks.
            IReadOnlyList<IModelProvider> resolvedCandidates;
            try
            {
                resolvedCandidates = providerResolver.ResolveCandidates(resolvedProviderName);
            }
            catch (InvalidOperationException)
            {
                throw CreateMissingProviderException(resolvedProviderName, requestedModel, "provider resolution");
            }

            var providerCandidates = new List<IModelProvider>();
            ProviderExecutionException? lastCandidateFailure = null;
            foreach (var candidate in resolvedCandidates)
            {
                var candidateModel = ResolveCandidateModel(
                    providerCatalogOptions.Value,
                    resolvedProviderName,
                    candidate.ProviderName,
                    requestedModel);
                try
                {
                    var authStatus = string.Equals(candidate.ProviderName, resolvedProviderName, StringComparison.OrdinalIgnoreCase)
                        ? await authFlowService.GetStatusAsync(candidate.ProviderName, cancellationToken).ConfigureAwait(false)
                        : await candidate.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
                    var authExpired = ProviderStreamFailureClassifier.IsExpired(authStatus, systemClock.UtcNow);
                    if (!authStatus.IsAuthenticated || authExpired)
                    {
                        var message = authExpired
                            ? $"Provider '{candidate.ProviderName}' authentication expired at {authStatus.ExpiresAtUtc:O}."
                            : $"Provider '{candidate.ProviderName}' is not authenticated.";
                        throw new ProviderExecutionException(
                            candidate.ProviderName,
                            candidateModel,
                            ProviderFailureKind.AuthenticationUnavailable,
                            message);
                    }

                    if (request.Context.UserContent?.Any(static block => block.Kind == ContentBlockKind.Image) == true
                        && !candidate.SupportsImageInput)
                    {
                        throw new ProviderExecutionException(
                            candidate.ProviderName,
                            candidateModel,
                            ProviderFailureKind.StreamFailed,
                            $"Provider '{candidate.ProviderName}' does not support structured image input.");
                    }

                    providerCandidates.Add(candidate);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ProviderExecutionException exception)
                {
                    lastCandidateFailure = exception;
                    logger.LogWarning(
                        exception,
                        "Skipping unavailable provider candidate {ProviderName} for session {SessionId}.",
                        candidate.ProviderName,
                        request.Context.SessionId);
                }
                catch (Exception exception)
                {
                    lastCandidateFailure = new ProviderExecutionException(
                        candidate.ProviderName,
                        candidateModel,
                        ProviderFailureKind.AuthenticationUnavailable,
                        $"Provider '{candidate.ProviderName}' authentication probe failed.",
                        exception);
                }
            }

            if (providerCandidates.Count == 0)
            {
                throw lastCandidateFailure ?? new ProviderExecutionException(
                    resolvedProviderName,
                    requestedModel,
                    ProviderFailureKind.AuthenticationUnavailable,
                    $"No authenticated provider was available for '{resolvedProviderName}'.");
            }

            var activeProviderIndex = 0;
            var activeProviderName = providerCandidates[0].ProviderName;
            var activeModel = ResolveCandidateModel(
                providerCatalogOptions.Value,
                resolvedProviderName,
                activeProviderName,
                requestedModel);

            // --- Build initial conversation messages ---
            // Do not add request.Instructions as a shared "system" chat message here.
            // Provider adapters apply system instructions via ProviderRequest.SystemPrompt
            // so providers that do not support a native "system" role (e.g. Anthropic)
            // do not receive duplicated or remapped instruction turns.
            var messages = new List<ChatMessage>();

            // Prepend prior-turn conversation history for multi-turn context.
            if (request.Context.ConversationHistory is { Count: > 0 } history)
            {
                messages.AddRange(history);
            }

            messages.Add(new ChatMessage(
                "user",
                request.Context.UserContent?.Count > 0
                    ? request.Context.UserContent
                    : [new ContentBlock(ContentBlockKind.Text, request.Context.Prompt, null, null, null, null)]));

            // --- Tool-calling loop ---
            var allProviderEvents = new List<ProviderEvent>();
            var allToolResults = new List<ToolResult>();
            var allToolEvents = new List<RuntimeEvent>();
            var outputSegments = new List<string>();
            UsageSnapshot? terminalUsage = null;
            ProviderRequest? lastProviderRequest = null;

            var iteration = 0;
            for (; iteration < options.MaxToolIterations; iteration++)
            {
                UsageSnapshot? iterationUsage = null;
                var iterationTextSegments = new List<string>();
                var toolUseEvents = new List<ProviderEvent>();
                Exception? lastStreamFailure = null;
                var streamSucceeded = false;

                for (var candidateIndex = activeProviderIndex; candidateIndex < providerCandidates.Count; candidateIndex++)
                {
                    var provider = providerCandidates[candidateIndex];
                    var candidateModel = ResolveCandidateModel(
                        providerCatalogOptions.Value,
                        resolvedProviderName,
                        provider.ProviderName,
                        requestedModel);
                    var providerRequest = providerRequestPreflight.Prepare(new ProviderRequest(
                        Id: $"provider-request-{Guid.NewGuid():N}",
                        SessionId: request.Context.SessionId,
                        TurnId: request.Context.TurnId,
                        ProviderName: provider.ProviderName,
                        Model: candidateModel,
                        Prompt: request.Context.Prompt,
                        SystemPrompt: request.Instructions,
                        OutputFormat: request.Context.OutputFormat,
                        Temperature: 0.1m,
                        Metadata: baseMetadata,
                        Messages: messages,
                        Tools: availableTools,
                        MaxTokens: options.MaxTokensPerRequest,
                        ContainsImageInput: messages.Any(static message => message.Content.Any(static block => block.Kind == ContentBlockKind.Image))));
                    var candidateEvents = new List<ProviderEvent>();
                    var candidateTextSegments = new List<string>();
                    var candidateToolUseEvents = new List<ProviderEvent>();
                    UsageSnapshot? candidateUsage = null;

                    using var providerScope = new ProviderActivityScope(provider.ProviderName, candidateModel, providerRequest.Id);
                    try
                    {
                        var stream = await provider.StartStreamAsync(providerRequest, cancellationToken).ConfigureAwait(false);
                        await foreach (var providerEvent in stream.Events.WithCancellation(cancellationToken))
                        {
                            candidateEvents.Add(providerEvent);
                            if (providerEvent.IsTerminal
                                && string.Equals(providerEvent.Kind, "failed", StringComparison.OrdinalIgnoreCase))
                            {
                                var failureKind = ProviderStreamFailureClassifier.ClassifyFailedEvent(providerEvent);
                                throw new ProviderExecutionException(
                                    provider.ProviderName,
                                    candidateModel,
                                    failureKind,
                                    CreateProviderFailedEventMessage(provider.ProviderName, providerEvent));
                            }

                            if (!providerEvent.IsTerminal && !string.IsNullOrWhiteSpace(providerEvent.Content))
                            {
                                candidateTextSegments.Add(providerEvent.Content);
                            }

                            if (!string.IsNullOrEmpty(providerEvent.ToolUseId) && !string.IsNullOrEmpty(providerEvent.ToolName))
                            {
                                candidateToolUseEvents.Add(providerEvent);
                            }

                            if (providerEvent.IsTerminal && providerEvent.Usage is not null)
                            {
                                candidateUsage = providerEvent.Usage;
                            }
                        }

                        providerScope.SetCompleted(candidateUsage?.InputTokens, candidateUsage?.OutputTokens);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        providerScope.SetError("Canceled by caller.");
                        throw;
                    }
                    catch (Exception exception)
                    {
                        providerScope.SetError(exception.Message);
                        lastStreamFailure = exception;
                        if (candidateIndex == providerCandidates.Count - 1)
                        {
                            break;
                        }

                        logger.LogWarning(
                            exception,
                            "Provider {ProviderName} failed; retrying the buffered iteration with fallback provider {FallbackProviderName}.",
                            provider.ProviderName,
                            providerCandidates[candidateIndex + 1].ProviderName);
                        continue;
                    }

                    allProviderEvents.AddRange(candidateEvents);
                    iterationTextSegments.AddRange(candidateTextSegments);
                    toolUseEvents.AddRange(candidateToolUseEvents);
                    iterationUsage = candidateUsage;
                    terminalUsage = candidateUsage ?? terminalUsage;
                    lastProviderRequest = providerRequest;
                    activeProviderIndex = candidateIndex;
                    activeProviderName = provider.ProviderName;
                    activeModel = candidateModel;
                    streamSucceeded = true;
                    break;
                }

                if (!streamSucceeded)
                {
                    throw lastStreamFailure ?? new ProviderExecutionException(
                        activeProviderName,
                        activeModel,
                        ProviderFailureKind.StreamFailed,
                        "All provider candidates failed before completing a response.");
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

            // Detect if the loop was exhausted (provider kept requesting tools every iteration).
            var toolLoopExhausted = iteration >= options.MaxToolIterations;
            if (toolLoopExhausted)
            {
                logger.LogWarning(
                    "Tool-calling loop reached maximum iterations ({MaxIterations}) for session {SessionId}; output may be incomplete.",
                    options.MaxToolIterations,
                    request.Context.SessionId);
                outputSegments.Add($"\n\n[Tool-calling loop reached the maximum of {options.MaxToolIterations} iterations. Output may be incomplete.]");
            }

            var output = string.Concat(outputSegments);
            if (string.IsNullOrWhiteSpace(output))
            {
                logger.LogWarning(
                    "Provider {ProviderName} returned no stream content for session {SessionId}; returning placeholder response.",
                    activeProviderName,
                    request.Context.SessionId);
                return CreatePlaceholderResult(request, activeModel, $"Provider '{activeProviderName}' returned no content; using placeholder response.");
            }

            var usage = terminalUsage ?? new UsageSnapshot(
                InputTokens: request.Context.Prompt.Length,
                OutputTokens: output.Length,
                CachedInputTokens: 0,
                TotalTokens: request.Context.Prompt.Length + output.Length,
                EstimatedCostUsd: null);

            var summary = toolLoopExhausted
                ? $"Provider response from {activeProviderName}/{activeModel} is incomplete because the tool-calling loop reached the maximum of {options.MaxToolIterations} iterations."
                : $"Streamed provider response from {activeProviderName}/{activeModel}.";

            return new ProviderInvocationResult(
                Output: output,
                Usage: usage,
                Summary: summary,
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

    private static string CreateProviderFailedEventMessage(string providerName, ProviderEvent providerEvent)
    {
        var detail = string.IsNullOrWhiteSpace(providerEvent.Content)
            ? "The provider stream ended with a failure event."
            : providerEvent.Content;
        return $"Provider '{providerName}' stream failed: {detail}";
    }

    private static string ResolveCandidateModel(
        ProviderCatalogOptions options,
        string primaryProviderName,
        string candidateProviderName,
        string primaryModel)
        => !string.Equals(candidateProviderName, primaryProviderName, StringComparison.OrdinalIgnoreCase)
            && options.FallbackModels.TryGetValue(candidateProviderName, out var fallbackModel)
            && !string.IsNullOrWhiteSpace(fallbackModel)
            ? fallbackModel
            : primaryModel;
}
