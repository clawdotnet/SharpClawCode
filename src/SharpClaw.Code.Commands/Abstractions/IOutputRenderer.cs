using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Renders CLI results for a specific output format.
/// </summary>
public interface IOutputRenderer
{
    /// <summary>
    /// Gets the supported output format.
    /// </summary>
    OutputFormat Format { get; }

    /// <summary>
    /// Renders a command result.
    /// </summary>
    /// <param name="result">The command result to render.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken);

    /// <summary>
    /// Renders a turn execution result.
    /// </summary>
    /// <param name="result">The turn execution result to render.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken);
}
