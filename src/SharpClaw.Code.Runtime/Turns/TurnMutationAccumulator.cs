using System.Collections.Concurrent;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>
/// Thread-safe collector for <see cref="FileMutationOperation"/> entries during a prompt turn.
/// </summary>
public sealed class TurnMutationAccumulator : IToolMutationRecorder
{
    private readonly ConcurrentQueue<FileMutationOperation> operations = new();

    /// <inheritdoc />
    public void Record(FileMutationOperation operation)
        => operations.Enqueue(operation);

    /// <summary>
    /// Returns captured operations in their recording order, preserving repeated edits to the same file.
    /// </summary>
    public IReadOnlyList<FileMutationOperation> ToSnapshot()
        => operations.ToArray();
}
