namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Runs out-of-turn session read-modify-write operations so they are serialized with each other and survive a
/// concurrently running prompt turn.
/// </summary>
public interface ISessionUpdateCoordinator
{
    /// <summary>
    /// Runs <paramref name="action"/> while holding the session's snapshot lock and recording its edits.
    /// The action must re-read the session through <see cref="ISessionStore"/> and save it there.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="workspacePath">Normalized workspace path.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="action">The read-modify-write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result.</returns>
    Task<T> RunAsync<T>(string workspacePath, string sessionId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);

    /// <summary>
    /// Clears recorded concurrent edits for a session (called by the turn runtime at turn boundaries).
    /// </summary>
    /// <param name="workspacePath">Normalized workspace path.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);
}
