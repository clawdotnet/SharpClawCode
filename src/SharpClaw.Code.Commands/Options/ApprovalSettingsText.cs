using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands.Options;

internal static class ApprovalSettingsText
{
    private static readonly ApprovalScope[] AllScopes =
    [
        ApprovalScope.ToolExecution,
        ApprovalScope.FileSystemWrite,
        ApprovalScope.ShellExecution,
        ApprovalScope.NetworkAccess,
        ApprovalScope.SessionOperation,
        ApprovalScope.PromptOutsideWorkspaceRead,
    ];

    public static ApprovalSettings? Parse(string? scopesText, int? budget)
    {
        var scopes = ParseScopes(scopesText);
        if (scopes is null && budget is null)
        {
            return null;
        }

        var normalizedBudget = budget is > 0 ? budget : null;
        return new ApprovalSettings(scopes ?? [], normalizedBudget);
    }

    public static IReadOnlyList<ApprovalScope>? ParseScopes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var tokens = value
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim().ToLowerInvariant())
            .ToArray();
        if (tokens.Length == 0)
        {
            return [];
        }

        if (tokens.Contains("none"))
        {
            return [];
        }

        var scopes = new List<ApprovalScope>();
        foreach (var token in tokens)
        {
            if (string.Equals(token, "all", StringComparison.Ordinal))
            {
                scopes.AddRange(AllScopes);
                continue;
            }

            scopes.Add(token switch
            {
                "tool" or "toolexecution" => ApprovalScope.ToolExecution,
                "file" or "write" or "filesystemwrite" => ApprovalScope.FileSystemWrite,
                "shell" or "shellexecution" => ApprovalScope.ShellExecution,
                "network" or "networkaccess" => ApprovalScope.NetworkAccess,
                "session" or "sessionoperation" => ApprovalScope.SessionOperation,
                "promptread" or "promptoutsideworkspaceread" => ApprovalScope.PromptOutsideWorkspaceRead,
                _ => throw new InvalidOperationException(
                    $"Unknown auto-approval scope '{token}'. Supported values: all, none, tool, file, shell, network, session, promptRead.")
            });
        }

        return scopes.Distinct().OrderBy(static scope => scope).ToArray();
    }

    public static string RenderSummary(ApprovalSettings? settings)
    {
        if (settings is null || settings.AutoApproveScopes.Count == 0)
        {
            return "none";
        }

        var scopes = string.Join(", ", settings.AutoApproveScopes);
        return settings.AutoApproveBudget is { } budget
            ? $"{scopes} (budget {budget})"
            : scopes;
    }
}
