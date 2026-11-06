namespace SharpClaw.Code.Runtime.Configuration;

/// <summary>Controls provider-facing output previews; original tool results and events remain complete.</summary>
public sealed class ToolOutputOptions
{
    /// <summary>Maximum UTF-16 characters per provider-facing tool result, including its artifact reference.</summary>
    public int MaxCharacters { get; set; } = 32_000;
}
