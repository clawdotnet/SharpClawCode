using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Recalls the most relevant structured memory entries for a prompt.
/// </summary>
public interface IMemoryRecallService
{
    /// <summary>
    /// Recalls project and user memory entries for the supplied prompt text.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> RecallAsync(
        string workspaceRoot,
        string prompt,
        int limit,
        CancellationToken cancellationToken);
}
