using System.Text.Json;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Cli.Rendering;

/// <summary>
/// Renders command and prompt results as JSON.
/// </summary>
public sealed class JsonOutputRenderer : IOutputRenderer
{
    /// <inheritdoc />
    public OutputFormat Format => OutputFormat.Json;

    /// <inheritdoc />
    public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
    {
        var json = string.IsNullOrWhiteSpace(result.DataJson)
            ? JsonSerializer.Serialize(result, ProtocolJsonContext.Default.CommandResult)
            : result.DataJson;

        return Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    /// <inheritdoc />
    public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.TurnExecutionResult);
        return Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
