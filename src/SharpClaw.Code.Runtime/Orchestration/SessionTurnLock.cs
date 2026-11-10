using System.Collections.Concurrent;
using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Serializes work on one session: an in-process mutex per workspace/session pair, then the session's cross-process turn lock file.
/// Prompt turns, undo/redo, share/unshare, and compaction all hold this lock so they never interleave.
/// </summary>
public sealed class SessionTurnLock(IFileSystem fileSystem, IRuntimeStoragePathResolver storagePathResolver)
{
    // Process-wide so separate runtime instances in one process still exclude each other.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionMutexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits for the session's in-process mutex, then its cross-process lock file.
    /// Disposing the result releases the lock file first and the mutex second.
    /// </summary>
    /// <param name="workspacePath">The normalized workspace path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token for both waits.</param>
    /// <returns>A handle that releases both locks when disposed.</returns>
    public async Task<IAsyncDisposable> AcquireAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        var sessionMutex = SessionMutexes.GetOrAdd($"{workspacePath}\u0000{sessionId}", static _ => new SemaphoreSlim(1, 1));
        await sessionMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var turnLockPath = storagePathResolver.GetSessionTurnLockPath(workspacePath, sessionId);
            var crossProcessTurnLock = await fileSystem
                .AcquireExclusiveFileLockAsync(turnLockPath, cancellationToken)
                .ConfigureAwait(false);
            return new Handle(crossProcessTurnLock, sessionMutex);
        }
        catch
        {
            sessionMutex.Release();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> while holding the session lock.
    /// </summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    /// <param name="workspacePath">The normalized workspace path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="operation">The work to run under the lock.</param>
    /// <param name="cancellationToken">The cancellation token passed to the waits and the operation.</param>
    /// <returns>The operation result.</returns>
    public async Task<T> ExecuteAsync<T>(
        string workspacePath,
        string sessionId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(operation);

        await using var sessionLock = await AcquireAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
        return await operation(cancellationToken).ConfigureAwait(false);
    }

    private sealed class Handle(IAsyncDisposable crossProcessTurnLock, SemaphoreSlim sessionMutex) : IAsyncDisposable
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await crossProcessTurnLock.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                sessionMutex.Release();
            }
        }
    }
}
