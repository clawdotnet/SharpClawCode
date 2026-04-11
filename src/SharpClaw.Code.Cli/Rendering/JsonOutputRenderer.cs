using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Cli.Rendering;

/// <summary>
/// Renders command and prompt results as JSON.
/// </summary>
public sealed class JsonOutputRenderer(ILogger<JsonOutputRenderer>? logger = null) : IOutputRenderer
{
    private readonly ILogger<JsonOutputRenderer> _logger = logger ?? NullLogger<JsonOutputRenderer>.Instance;
    /// <inheritdoc />
    public OutputFormat Format => OutputFormat.Json;

    /// <inheritdoc />
    public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
    {
        JsonElement? data = null;
        string? dataRaw = null;
        if (!string.IsNullOrWhiteSpace(result.DataJson))
        {
            try
            {
                using var document = JsonDocument.Parse(result.DataJson);
                data = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse DataJson as valid JSON; falling back to raw string.");
                dataRaw = result.DataJson;
            }
        }

        var json = JsonSerializer.Serialize(
            new JsonCommandEnvelope(
                result.Succeeded,
                result.ExitCode,
                OutputFormat.Json,
                result.Message,
                data,
                dataRaw),
            JsonOutputJsonContext.Default.JsonCommandEnvelope);

        return Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    /// <inheritdoc />
    public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.TurnExecutionResult);
        return Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}

internal sealed record JsonCommandEnvelope(
    bool Succeeded,
    int ExitCode,
    OutputFormat OutputFormat,
    string Message,
    JsonElement? Data,
    string? DataRaw);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(JsonCommandEnvelope))]
internal sealed partial class JsonOutputJsonContext : JsonSerializerContext;
