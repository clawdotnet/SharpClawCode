using System.Text.Json;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Runtime.Workflow;

internal static class ApprovalSettingsResolver
{
    public static ApprovalSettings? ResolveEffective(RunPromptRequest request, ConversationSession session)
    {
        if (request.ApprovalSettings is not null)
        {
            return Normalize(request.ApprovalSettings);
        }

        if (session.Metadata is null)
        {
            return null;
        }

        var scopes = ReadScopes(session.Metadata);
        var budget = ReadBudget(session.Metadata);
        return scopes is null && budget is null
            ? null
            : Normalize(new ApprovalSettings(scopes ?? [], budget));
    }

    public static ApprovalSettings Normalize(ApprovalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var scopes = settings.AutoApproveScopes
            .Where(scope => scope != ApprovalScope.None)
            .Distinct()
            .OrderBy(static scope => scope)
            .ToArray();
        var budget = settings.AutoApproveBudget is > 0 ? settings.AutoApproveBudget : null;

        return scopes.Length == 0 && budget is null
            ? ApprovalSettings.Empty
            : new ApprovalSettings(scopes, budget);
    }

    private static IReadOnlyList<ApprovalScope>? ReadScopes(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.ListApprovalScope);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadBudget(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget, out var payload)
           && int.TryParse(payload, out var parsed)
           && parsed > 0
            ? parsed
            : null;
}
