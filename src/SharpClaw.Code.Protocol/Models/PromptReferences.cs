namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Kind of <c>@</c> prompt reference (extensible).
/// </summary>
public enum PromptReferenceKind
{
    /// <summary>File path reference.</summary>
    File,
}

/// <summary>
/// One resolved <c>@path</c> attachment in a prompt.
/// </summary>
public sealed record PromptReference(
    PromptReferenceKind Kind,
    string OriginalToken,
    string RequestedPath,
    string ResolvedFullPath,
    string DisplayPath,
    bool WasOutsideWorkspace,
    string IncludedContent);

/// <summary>
/// Result of expanding all <c>@file</c> tokens in a prompt.
/// </summary>
public sealed record PromptReferenceResolution(
    string OriginalPrompt,
    string ExpandedPrompt,
    List<PromptReference> References);
