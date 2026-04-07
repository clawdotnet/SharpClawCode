using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Expands <c>@path</c> prompt references with permission-aware file reads.
/// </summary>
public interface IPromptReferenceResolver
{
    /// <summary>
    /// Expands every path reference in the request prompt and returns structured metadata.
    /// </summary>
    /// <param name="workspaceRoot">Normalized workspace root.</param>
    /// <param name="workingDirectory">Base directory for relative paths.</param>
    /// <param name="session">The active session.</param>
    /// <param name="turn">The active turn.</param>
    /// <param name="request">The prompt request providing permission mode and metadata.</param>
    /// <param name="primaryMode">Effective primary workflow mode.</param>
    /// <param name="isInteractive">Whether approval prompts are allowed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Original prompt, expanded prompt, and resolved references.</returns>
    /// <exception cref="InvalidOperationException">When a reference is missing, unreadable, or denied.</exception>
    Task<PromptReferenceResolution> ResolveAsync(
        string workspaceRoot,
        string workingDirectory,
        ConversationSession session,
        ConversationTurn turn,
        RunPromptRequest request,
        PrimaryMode primaryMode,
        bool isInteractive,
        CancellationToken cancellationToken);
}
