using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Internal;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Streams responses from an OpenAI-compatible chat completions API using Microsoft.Extensions.AI and the OpenAI .NET SDK.
/// </summary>
public sealed class OpenAiCompatibleProvider(
    IOptions<OpenAiCompatibleProviderOptions> options,
    ISystemClock systemClock,
    ILogger<OpenAiCompatibleProvider> logger) : IModelProvider
{
    private readonly OpenAiCompatibleProviderOptions _options = options.Value;
    private OpenAIClient? _cachedOpenAiClient;

    /// <inheritdoc />
    public string ProviderName => _options.ProviderName;

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(Internal.ProviderAuthStatusFactory.FromApiKeyPresence(ProviderName, _options.ApiKey));

    /// <inheritdoc />
    public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting OpenAI-compatible MEAI stream for request {RequestId}.", request.Id);
        return Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request, cancellationToken)));
    }

    private async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
        ProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var modelId = Internal.ProviderHttpHelpers.ResolveModelOrDefault(request.Model, _options.DefaultModel);
        var openAiClient = GetOrCreateOpenAiClient();
        var nativeClient = openAiClient.GetChatClient(modelId);
        using var chatClient = nativeClient.AsIChatClient();

        var messages = request.Messages is not null
            ? OpenAiMessageBuilder.BuildMessages(request.Messages)
            : BuildChatMessages(request);

        var chatOptions = new ChatOptions();
        if (request.Temperature is { } temp)
        {
            chatOptions.Temperature = (float)temp;
        }

        if (request.MaxTokens is { } maxTokens)
        {
            chatOptions.MaxOutputTokens = maxTokens;
        }

        if (request.Tools is { Count: > 0 } toolDefs)
        {
            chatOptions.Tools = OpenAiMessageBuilder.BuildTools(toolDefs);
        }

        var updates = chatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken);
        await foreach (var ev in OpenAiMeaiStreamAdapter.AdaptAsync(updates, request.Id, systemClock, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return ev;
        }
    }

    private OpenAIClient GetOrCreateOpenAiClient()
    {
        if (_cachedOpenAiClient is not null)
        {
            return _cachedOpenAiClient;
        }

        var openAiOptions = new OpenAIClientOptions();
        var normalized = Internal.ProviderHttpHelpers.NormalizeBaseUrl(_options.BaseUrl);
        if (normalized is not null)
        {
            openAiOptions.Endpoint = new Uri(normalized);
        }

        var credential = new ApiKeyCredential(_options.ApiKey ?? string.Empty);
        _cachedOpenAiClient = new OpenAIClient(credential, openAiOptions);
        return _cachedOpenAiClient;
    }

    private static List<Microsoft.Extensions.AI.ChatMessage> BuildChatMessages(ProviderRequest request)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, request.SystemPrompt));
        }

        messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, request.Prompt));
        return messages;
    }
}
