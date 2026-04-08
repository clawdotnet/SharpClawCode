using Microsoft.Extensions.Logging;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Agents.Internal;

/// <summary>
/// Executes the provider-backed core of a SharpClaw agent run.
/// </summary>
public sealed class ProviderBackedAgentKernel(
    IProviderRequestPreflight providerRequestPreflight,
    IModelProviderResolver providerResolver,
    IAuthFlowService authFlowService,
    ILogger<ProviderBackedAgentKernel> logger)
{
    internal async Task<ProviderInvocationResult> ExecuteAsync(AgentFrameworkRequest request, CancellationToken cancellationToken)
    {
        var requestedModel = request.Context.Model;
        var requestedProvider = request.Context.Metadata is not null && request.Context.Metadata.TryGetValue("provider", out var metadataProvider)
            ? metadataProvider
            : string.Empty;

        var providerRequest = providerRequestPreflight.Prepare(new ProviderRequest(
            Id: $"provider-request-{Guid.NewGuid():N}",
            SessionId: request.Context.SessionId,
            TurnId: request.Context.TurnId,
            ProviderName: requestedProvider ?? string.Empty,
            Model: requestedModel,
            Prompt: request.Context.Prompt,
            SystemPrompt: request.Instructions,
            OutputFormat: request.Context.OutputFormat,
            Temperature: 0.1m,
            Metadata: request.Context.Metadata is null
                ? null
                : new Dictionary<string, string>(request.Context.Metadata, StringComparer.Ordinal)));

        try
        {
            AuthStatus authStatus;
            try
            {
                authStatus = await authFlowService.GetStatusAsync(providerRequest.ProviderName, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                throw CreateMissingProviderException(providerRequest.ProviderName, requestedModel, "auth status lookup");
            }
            catch (Exception exception)
            {
                throw new ProviderExecutionException(
                    providerRequest.ProviderName,
                    requestedModel,
                    ProviderFailureKind.AuthenticationUnavailable,
                    $"Provider '{providerRequest.ProviderName}' authentication probe failed.",
                    exception);
            }

            if (!authStatus.IsAuthenticated)
            {
                logger.LogWarning(
                    "Provider {ProviderName} is not authenticated for session {SessionId}.",
                    providerRequest.ProviderName,
                    request.Context.SessionId);
                throw new ProviderExecutionException(
                    providerRequest.ProviderName,
                    providerRequest.Model,
                    ProviderFailureKind.AuthenticationUnavailable,
                    $"Provider '{providerRequest.ProviderName}' is not authenticated.");
            }

            IModelProvider provider;
            try
            {
                provider = providerResolver.Resolve(providerRequest.ProviderName);
            }
            catch (InvalidOperationException)
            {
                throw CreateMissingProviderException(providerRequest.ProviderName, requestedModel, "provider resolution");
            }

            var stream = await provider.StartStreamAsync(providerRequest, cancellationToken).ConfigureAwait(false);
            var providerEvents = new List<ProviderEvent>();
            var outputSegments = new List<string>();
            UsageSnapshot? terminalUsage = null;

            await foreach (var providerEvent in stream.Events.WithCancellation(cancellationToken))
            {
                providerEvents.Add(providerEvent);

                if (!providerEvent.IsTerminal && !string.IsNullOrWhiteSpace(providerEvent.Content))
                {
                    outputSegments.Add(providerEvent.Content);
                }

                if (providerEvent.IsTerminal && providerEvent.Usage is not null)
                {
                    terminalUsage = providerEvent.Usage;
                }
            }

            var output = string.Concat(outputSegments);
            if (string.IsNullOrWhiteSpace(output))
            {
                logger.LogWarning(
                    "Provider {ProviderName} returned no stream content for session {SessionId}; returning placeholder response.",
                    providerRequest.ProviderName,
                    request.Context.SessionId);
                return CreatePlaceholderResult(request, providerRequest.Model, $"Provider '{providerRequest.ProviderName}' returned no content; using placeholder response.");
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
                Summary: $"Streamed provider response from {providerRequest.ProviderName}/{providerRequest.Model}.",
                ProviderRequest: providerRequest,
                ProviderEvents: providerEvents);
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
                providerRequest.ProviderName,
                providerRequest.Model,
                ProviderFailureKind.StreamFailed,
                $"Provider '{providerRequest.ProviderName}' failed during execution.",
                exception);
        }
    }

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
