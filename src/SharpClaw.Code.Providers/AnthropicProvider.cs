using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Internal;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Streams responses from Anthropic's Messages API using the official Anthropic C# SDK.
/// </summary>
public sealed class AnthropicProvider(
    IOptions<AnthropicProviderOptions> options,
    IProviderCredentialStore credentialStore,
    ISystemClock systemClock,
    ILogger<AnthropicProvider> logger) : IModelProvider
{
    private readonly AnthropicProviderOptions _options = options.Value;

    /// <inheritdoc />
    public string ProviderName => _options.ProviderName;

    /// <inheritdoc />
    public bool SupportsImageInput => _options.SupportsImageInput;

    /// <inheritdoc />
    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        var resolved = await ResolveCredentialAsync(cancellationToken).ConfigureAwait(false);
        return Internal.ProviderAuthStatusFactory.FromConfiguration(
            ProviderName,
            resolved.ApiKey,
            ProviderAuthMode.ApiKey,
            hasAuthOptionalRuntime: false,
            sourceType: resolved.SourceType ?? (string.IsNullOrWhiteSpace(_options.ApiKey) ? null : "config"),
            statusDetail: resolved.StatusDetail ?? (string.IsNullOrWhiteSpace(_options.ApiKey) ? null : "configured API key"));
    }

    /// <inheritdoc />
    public async Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = await ResolveCredentialAsync(cancellationToken).ConfigureAwait(false);
        var client = CreateClient(resolved.ApiKey);
        var modelId = Internal.ProviderHttpHelpers.ResolveModelOrDefault(request.Model, _options.DefaultModel);

        var parameters = CreateMessageParameters(request, modelId);

        logger.LogInformation("Starting Anthropic SDK stream for request {RequestId}.", request.Id);

        IAsyncEnumerable<RawMessageStreamEvent> stream;
        try
        {
            stream = client.Messages.CreateStreaming(parameters, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (ProviderStreamFailureClassifier.IsAuthenticationFailure(exception))
        {
            throw new ProviderExecutionException(
                ProviderName,
                modelId,
                ProviderFailureKind.AuthenticationUnavailable,
                $"Provider '{ProviderName}' authentication failed while starting the stream.",
                exception);
        }

        logger.LogInformation("Started Anthropic SDK stream for request {RequestId}.", request.Id);

        return new ProviderStreamHandle(request, AnthropicSdkStreamAdapter.AdaptAsync(stream, request.Id, systemClock, cancellationToken));
    }

    /// <summary>
    /// Builds SDK parameters without the obsolete temperature field, which newer models reject.
    /// </summary>
    internal static MessageCreateParams CreateMessageParameters(ProviderRequest request, string modelId)
    {
        var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt;

        MessageCreateParams parameters;

        if (request.Messages is not null)
        {
            var messages = Internal.AnthropicMessageBuilder.BuildMessages(request.Messages);
            parameters = new MessageCreateParams
            {
                MaxTokens = request.MaxTokens ?? 1024,
                Model = modelId,
                Messages = messages,
            };

            if (request.Tools is { Count: > 0 } tools)
            {
                var anthropicTools = Internal.AnthropicMessageBuilder.BuildTools(tools);
                parameters = parameters with { Tools = anthropicTools };
            }
        }
        else
        {
            parameters = new MessageCreateParams
            {
                MaxTokens = request.MaxTokens ?? 1024,
                Model = modelId,
                Messages =
                [
                    new MessageParam
                    {
                        Role = Role.User,
                        Content = request.Prompt,
                    },
                ],
            };
        }

        if (systemPrompt is not null)
        {
            parameters = parameters with { System = systemPrompt };
        }

        return parameters;
    }

    private AnthropicClient CreateClient(string? resolvedApiKey)
    {
        var apiKey = resolvedApiKey ?? _options.ApiKey ?? string.Empty;
        var clientOptions = new ClientOptions
        {
            ApiKey = apiKey,
        };

        var normalized = Internal.ProviderHttpHelpers.NormalizeBaseUrl(_options.BaseUrl);
        if (normalized is not null)
        {
            clientOptions.BaseUrl = normalized;
        }

        return new AnthropicClient(clientOptions);
    }

    private async Task<ResolvedProviderCredential> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new ResolvedProviderCredential(_options.ApiKey, "config", "configured API key");
        }

        return await credentialStore.ResolveAsync(ProviderName, cancellationToken).ConfigureAwait(false);
    }
}
