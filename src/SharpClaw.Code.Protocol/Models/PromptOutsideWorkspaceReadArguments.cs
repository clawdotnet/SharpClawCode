namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Arguments for the <c>prompt-outside-workspace-read</c> permission gate tool.
/// </summary>
/// <param name="Path">Absolute path requested for read.</param>
public sealed record PromptOutsideWorkspaceReadArguments(string Path);
