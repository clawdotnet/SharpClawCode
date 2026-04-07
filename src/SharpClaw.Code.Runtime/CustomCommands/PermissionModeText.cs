using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Runtime.CustomCommands;

/// <summary>
/// Parses permission mode text from markdown frontmatter.
/// </summary>
internal static class PermissionModeText
{
    public static PermissionMode? TryParseOptional(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read-only" => PermissionMode.ReadOnly,
            "workspacewrite" or "workspace-write" or "prompt" or "autoapprovesafe" or "auto-approve-safe" => PermissionMode.WorkspaceWrite,
            "dangerfullaccess" or "danger-full-access" or "fulltrust" or "full-trust" => PermissionMode.DangerFullAccess,
            _ => null,
        };
    }
}
