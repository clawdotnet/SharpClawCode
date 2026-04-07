using SharpClaw.Code.Commands.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Hosts the interactive REPL loop.
/// </summary>
public interface IReplHost
{
    /// <summary>
    /// Runs the REPL loop using the specified execution context.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(CommandExecutionContext context, CancellationToken cancellationToken);
}
