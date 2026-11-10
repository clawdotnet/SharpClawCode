using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Selects the effective event backend from the current runtime host context.
/// </summary>
public sealed class HostAwareEventStore(
    NdjsonEventStore fileEventStore,
    SqliteEventStore sqliteEventStore,
    IRuntimeHostContextAccessor hostContextAccessor) : IEventStore
{
    /// <inheritdoc />
    public Task AppendAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        => ResolveStore().AppendAsync(workspacePath, sessionId, runtimeEvent, cancellationToken);

    /// <inheritdoc />
    public Task AppendRangeAsync(string workspacePath, string sessionId, IReadOnlyList<RuntimeEvent> runtimeEvents, CancellationToken cancellationToken)
        => ResolveStore().AppendRangeAsync(workspacePath, sessionId, runtimeEvents, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
        => ResolveStore().ReadAllAsync(workspacePath, sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RuntimeEvent>> ReadLatestAsync(string workspacePath, string sessionId, int count, CancellationToken cancellationToken)
        => ResolveStore().ReadLatestAsync(workspacePath, sessionId, count, cancellationToken);

    private IEventStore ResolveStore()
        => hostContextAccessor.Current?.SessionStoreKind == SessionStoreKind.Sqlite
            ? sqliteEventStore
            : fileEventStore;
}
