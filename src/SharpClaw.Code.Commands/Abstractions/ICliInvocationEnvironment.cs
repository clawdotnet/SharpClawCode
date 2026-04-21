namespace SharpClaw.Code.Commands;

/// <summary>
/// Describes standard-input and standard-output characteristics for CLI invocation routing.
/// </summary>
public interface ICliInvocationEnvironment
{
    /// <summary>
    /// Gets a value indicating whether standard input is redirected.
    /// </summary>
    bool IsInputRedirected { get; }

    /// <summary>
    /// Gets a value indicating whether standard output is redirected.
    /// </summary>
    bool IsOutputRedirected { get; }

    /// <summary>
    /// Reads all available standard input content.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete standard input payload.</returns>
    Task<string> ReadStandardInputToEndAsync(CancellationToken cancellationToken);
}
