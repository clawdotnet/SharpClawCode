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
    ILogger<WebhookRuntimeEventSink>? logger = null) : IRuntimeEventSink
{
    private readonly TelemetryOptions telemetryOptions = telemetryOptionsAccessor.Value;
    private readonly ILogger<WebhookRuntimeEventSink> logger = logger ?? NullLogger<WebhookRuntimeEventSink>.Instance;
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    /// <inheritdoc />
    public async Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (telemetryOptions.EventWebhookUrls.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(envelope, ProtocolJsonContext.Default.RuntimeEventEnvelope);
        foreach (var url in telemetryOptions.EventWebhookUrls)
        {
            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to post runtime event {EventId} to webhook {WebhookUrl}.", envelope.Event.EventId, url);
            }
        }
    }
}
