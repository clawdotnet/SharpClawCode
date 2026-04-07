using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Runtime.CustomCommands;

internal static class PrimaryModeText
{
    public static PrimaryMode? TryParseOptional(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Enum.TryParse<PrimaryMode>(text.Trim(), ignoreCase: true, out var mode)
            ? mode
            : null;
    }
}
