using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Concurrency;

/// <summary>
/// Default <see cref="ISessionUpdateCoordinator"/> backed by <see cref="SessionUpdateScope"/>.
/// </summary>
public sealed class SessionUpdateCoordinator(
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : ISessionUpdateCoordinator
{
    /// <inheritdoc />
    public Task<T> RunAsync<T>(string workspacePath, string sessionId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => SessionUpdateScope.RunAsync(fileSystem, storagePathResolver, workspacePath, sessionId, action, cancellationToken);

    /// <inheritdoc />
    public Task ResetAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
        => SessionUpdateScope.ResetOverlayAsync(fileSystem, storagePathResolver, workspacePath, sessionId, cancellationToken);
}
