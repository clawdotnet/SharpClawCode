using System.Globalization;
using System.Text.Json;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Identifier and session-metadata helpers shared by the turn pipeline and session admin commands.
/// </summary>
internal static class SessionMetadataHelpers
{
    internal const string LastTurnSequenceKey = "lastTurnSequence";

    internal static string CreateIdentifier(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";

    internal static int GetLastTurnSequence(ConversationSession session)
        => session.Metadata is not null
           && session.Metadata.TryGetValue(LastTurnSequenceKey, out var sequenceText)
           && int.TryParse(sequenceText, CultureInfo.InvariantCulture, out var sequence)
            ? sequence
            : 0;

    internal static Dictionary<string, string> CloneMetadata(Dictionary<string, string>? metadata)
        => metadata is null
            ? []
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

    internal static Dictionary<string, string> MergeMetadata(
        Dictionary<string, string>? metadata,
        PermissionMode permissionMode,
        OutputFormat outputFormat,
        PrimaryMode primaryMode,
        ApprovalSettings? approvalSettings)
    {
        var merged = CloneMetadata(metadata);
        merged["permissionMode"] = permissionMode.ToString();
        merged["outputFormat"] = outputFormat.ToString();
        merged[SharpClawWorkflowMetadataKeys.PrimaryMode] = primaryMode.ToString();
        if (approvalSettings is not null && approvalSettings.AutoApproveScopes.Count > 0)
        {
            merged[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson] = JsonSerializer.Serialize(
                approvalSettings.AutoApproveScopes.ToList(),
                ProtocolJsonContext.Default.ListApprovalScope);
        }
        else
        {
            merged.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson);
        }

        if (approvalSettings?.AutoApproveBudget is { } budget)
        {
            merged[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget] = budget.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            merged.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget);
        }

        return merged;
    }
}
