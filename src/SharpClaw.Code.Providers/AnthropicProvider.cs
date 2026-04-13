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
    ISystemClock systemClock,
    ILogger<AnthropicProvider> logger) : IModelProvider
{
    private readonly AnthropicProviderOptions _options = options.Value;

    /// <inheritdoc />
    public string ProviderName => _options.ProviderName;

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(Internal.ProviderAuthStatusFactory.FromApiKeyPresence(ProviderName, _options.ApiKey));

    /// <inheritdoc />
    public async Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        var modelId = Internal.ProviderHttpHelpers.ResolveModelOrDefault(request.Model, _options.DefaultModel);

        var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt;
        float? temperature = request.Temperature.HasValue ? (float)request.Temperature.Value : null;

        MessageCreateParams parameters;

        if (request.Messages is not null)
        {
            var messages = Internal.AnthropicMessageBuilder.BuildMessages(request.Messages);
            parameters = new MessageCreateParams
            {
                MaxTokens = request.MaxTokens ?? 1024,
                Model = modelId,
                Messages = messages,
                Temperature = temperature,
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
                Temperature = temperature,
            };
        }

        if (systemPrompt is not null)
        {
            parameters = parameters with { System = systemPrompt };
        }

        logger.LogInformation("Started Anthropic SDK stream for request {RequestId}.", request.Id);

        var stream = client.Messages.CreateStreaming(parameters, cancellationToken);
        return new ProviderStreamHandle(request, AnthropicSdkStreamAdapter.AdaptAsync(stream, request.Id, systemClock, cancellationToken));
    }

    private AnthropicClient CreateClient()
    {
        var apiKey = _options.ApiKey ?? string.Empty;
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
}
