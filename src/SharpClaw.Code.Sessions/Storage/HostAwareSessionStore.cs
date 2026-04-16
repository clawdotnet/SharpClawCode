using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Selects the effective session backend from the current runtime host context.
/// </summary>
public sealed class HostAwareSessionStore(
    FileSessionStore fileSessionStore,
    SqliteSessionStore sqliteSessionStore,
    IRuntimeHostContextAccessor hostContextAccessor) : ISessionStore
{
    /// <inheritdoc />
    public Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
        => ResolveStore().SaveAsync(workspacePath, session, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
        => ResolveStore().GetByIdAsync(workspacePath, sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
        => ResolveStore().GetLatestAsync(workspacePath, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
        => ResolveStore().ListAllAsync(workspacePath, cancellationToken);

    private ISessionStore ResolveStore()
        => hostContextAccessor.Current?.SessionStoreKind == SessionStoreKind.Sqlite
            ? sqliteSessionStore
            : fileSessionStore;
}
