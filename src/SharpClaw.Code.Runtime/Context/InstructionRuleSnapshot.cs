namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Captures a single persistent instruction document loaded for prompt context.
/// </summary>
/// <param name="SourceKind">The rule source family, such as <c>workspace</c> or <c>global</c>.</param>
/// <param name="DisplayPath">The display path shown in prompt context.</param>
/// <param name="Content">The normalized rule content.</param>
/// <param name="IsTruncated">Whether the content was trimmed to fit prompt budget.</param>
public sealed record InstructionRuleDocument(
    string SourceKind,
    string DisplayPath,
    string Content,
    bool IsTruncated);

/// <summary>
/// Snapshot of instruction documents discovered for a workspace.
/// </summary>
/// <param name="Documents">The ordered instruction documents.</param>
public sealed record InstructionRuleSnapshot(
    IReadOnlyList<InstructionRuleDocument> Documents);
