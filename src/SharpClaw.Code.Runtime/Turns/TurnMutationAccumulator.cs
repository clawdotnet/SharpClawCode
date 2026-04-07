using System.Collections.Concurrent;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>
/// Thread-safe collector for <see cref="FileMutationOperation"/> entries during a prompt turn.
/// </summary>
public sealed class TurnMutationAccumulator : IToolMutationRecorder
{
    private readonly ConcurrentBag<FileMutationOperation> operations = new();

    /// <inheritdoc />
    public void Record(FileMutationOperation operation)
        => operations.Add(operation);

    /// <summary>
    /// Returns captured operations in an arbitrary order; consumers should sort by <see cref="FileMutationOperation.OperationId"/> if deterministic ordering is required.
    /// For current tools, operations are naturally sequential per turn execution thread.
    /// </summary>
    public IReadOnlyList<FileMutationOperation> ToSnapshot()
        => operations.OrderBy(o => o.OperationId, StringComparer.Ordinal).ToArray();
}
