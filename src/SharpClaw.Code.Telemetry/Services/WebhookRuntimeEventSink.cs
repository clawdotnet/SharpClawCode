using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// Delivers runtime event envelopes to configured webhook endpoints.
/// </summary>
public sealed class WebhookRuntimeEventSink(
    IOptions<TelemetryOptions> telemetryOptionsAccessor,
    HttpClient httpClient,
    IWebhookDelayStrategy webhookDelayStrategy,
    ILogger<WebhookRuntimeEventSink>? logger = null) : IRuntimeEventSink
{
    private readonly TelemetryOptions telemetryOptions = telemetryOptionsAccessor.Value;
    private readonly HttpClient httpClient = httpClient;
    private readonly IWebhookDelayStrategy webhookDelayStrategy = webhookDelayStrategy;
    private readonly ILogger<WebhookRuntimeEventSink> logger = logger ?? NullLogger<WebhookRuntimeEventSink>.Instance;

    /// <inheritdoc />
    public async Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (telemetryOptions.EventWebhookUrls.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(envelope, ProtocolJsonContext.Default.RuntimeEventEnvelope);
        var maxAttempts = Math.Max(1, telemetryOptions.WebhookMaxAttempts);
        foreach (var url in telemetryOptions.EventWebhookUrls)
        {
            var attempt = 0;
            while (attempt++ < maxAttempts)
            {
                try
                {
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var response = await httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (attempt < maxAttempts)
                {
                    logger.LogWarning(
                        exception,
                        "Retrying runtime event {EventId} webhook delivery to {WebhookUrl} after attempt {Attempt}.",
                        envelope.Event.EventId,
                        url,
                        attempt);
                    var delay = TimeSpan.FromMilliseconds(telemetryOptions.WebhookInitialBackoffMilliseconds * Math.Pow(2, attempt - 1));
                    await webhookDelayStrategy.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to post runtime event {EventId} to webhook {WebhookUrl}.", envelope.Event.EventId, url);
                    break;
                }
            }
        }
    }
}
