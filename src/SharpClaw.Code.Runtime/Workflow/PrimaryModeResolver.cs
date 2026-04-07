using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Resolves the effective <see cref="PrimaryMode"/> for a prompt execution.
/// </summary>
internal static class PrimaryModeResolver
{
    /// <summary>
    /// Prefers <see cref="RunPromptRequest.PrimaryMode"/>, then session metadata, then build mode.
    /// </summary>
    public static PrimaryMode ResolveEffective(RunPromptRequest request, ConversationSession session)
    {
        if (request.PrimaryMode is { } fromRequest)
        {
            return fromRequest;
        }

        if (session.Metadata is not null
            && session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.PrimaryMode, out var stored)
            && Enum.TryParse<PrimaryMode>(stored, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return PrimaryMode.Build;
    }
}
