using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Selects the appropriate output renderer for command and turn results.
/// </summary>
public sealed class OutputRendererDispatcher(IEnumerable<IOutputRenderer> renderers)
{
    private readonly IReadOnlyDictionary<OutputFormat, IOutputRenderer> _renderers = renderers.ToDictionary(renderer => renderer.Format);

    /// <summary>
    /// Renders a command result in the requested output format.
    /// </summary>
    /// <param name="result">The command result to render.</param>
    /// <param name="format">The output format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RenderCommandResultAsync(CommandResult result, OutputFormat format, CancellationToken cancellationToken)
        => Resolve(format).RenderCommandResultAsync(result, cancellationToken);

    /// <summary>
    /// Renders a turn execution result in the requested output format.
    /// </summary>
    /// <param name="result">The turn execution result to render.</param>
    /// <param name="format">The output format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, OutputFormat format, CancellationToken cancellationToken)
        => Resolve(format).RenderTurnExecutionResultAsync(result, cancellationToken);

    private IOutputRenderer Resolve(OutputFormat format)
        => _renderers.TryGetValue(format, out var renderer)
            ? renderer
            : throw new InvalidOperationException($"No renderer registered for format '{format}'.");
}
