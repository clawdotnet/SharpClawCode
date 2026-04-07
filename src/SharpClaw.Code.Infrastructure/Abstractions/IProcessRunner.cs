using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Runs external processes and captures their output.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a process asynchronously.
    /// </summary>
    /// <param name="request">The process request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process result.</returns>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}
