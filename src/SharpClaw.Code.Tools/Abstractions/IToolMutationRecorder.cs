using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Tools.Abstractions;

/// <summary>
/// Collects reversible file mutations performed during a SharpClaw tool execution turn.
/// </summary>
public interface IToolMutationRecorder
{
    /// <summary>
    /// Records a successful mutation after the tool has written storage.
    /// </summary>
    void Record(FileMutationOperation operation);
}
