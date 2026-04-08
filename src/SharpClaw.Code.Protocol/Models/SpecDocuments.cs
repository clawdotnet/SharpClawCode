namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// A single EARS-style requirement line item.
/// </summary>
/// <param name="Id">Stable identifier such as <c>REQ-001</c>.</param>
/// <param name="Statement">Full EARS-style requirement statement.</param>
/// <param name="Rationale">Optional rationale or context.</param>
public sealed record SpecRequirementItem(
    string Id,
    string Statement,
    string? Rationale);

/// <summary>
/// Structured requirements document used for spec generation and rendering.
/// </summary>
/// <param name="Title">Human-readable feature or capability title.</param>
/// <param name="Summary">Short summary of the feature intent.</param>
/// <param name="Requirements">Ordered EARS-style requirements.</param>
public sealed record SpecRequirementsDocument(
    string Title,
    string Summary,
    List<SpecRequirementItem> Requirements);

/// <summary>
/// Structured technical design document used for spec generation and rendering.
/// </summary>
/// <param name="Title">Human-readable design title.</param>
/// <param name="Summary">Brief technical summary.</param>
/// <param name="Architecture">Key architecture decisions or components.</param>
/// <param name="DataFlow">Important data and execution flow points.</param>
/// <param name="Interfaces">Notable APIs, contracts, or integration seams.</param>
/// <param name="FailureModes">Important failure cases and mitigations.</param>
/// <param name="Testing">Validation and test strategy.</param>
public sealed record SpecDesignDocument(
    string Title,
    string Summary,
    List<string> Architecture,
    List<string> DataFlow,
    List<string> Interfaces,
    List<string> FailureModes,
    List<string> Testing);

/// <summary>
/// A single implementation task item for a generated spec.
/// </summary>
/// <param name="Id">Stable identifier such as <c>TASK-001</c>.</param>
/// <param name="Description">Actionable task description.</param>
/// <param name="DoneCriteria">Optional completion guidance.</param>
public sealed record SpecTaskItem(
    string Id,
    string Description,
    string? DoneCriteria);

/// <summary>
/// Structured task document used for spec generation and rendering.
/// </summary>
/// <param name="Title">Human-readable task plan title.</param>
/// <param name="Tasks">Ordered implementation tasks.</param>
public sealed record SpecTasksDocument(
    string Title,
    List<SpecTaskItem> Tasks);

/// <summary>
/// Structured model output payload for spec-mode prompt execution.
/// </summary>
/// <param name="Requirements">Requirements document content.</param>
/// <param name="Design">Technical design content.</param>
/// <param name="Tasks">Implementation tasks content.</param>
public sealed record SpecGenerationPayload(
    SpecRequirementsDocument Requirements,
    SpecDesignDocument Design,
    SpecTasksDocument Tasks);

/// <summary>
/// Metadata describing the generated spec artifact set written to disk.
/// </summary>
/// <param name="Slug">The slug used for the generated spec folder.</param>
/// <param name="RootPath">Absolute path to the generated spec folder.</param>
/// <param name="RequirementsPath">Absolute path to <c>requirements.md</c>.</param>
/// <param name="DesignPath">Absolute path to <c>design.md</c>.</param>
/// <param name="TasksPath">Absolute path to <c>tasks.md</c>.</param>
/// <param name="GeneratedAtUtc">When the artifact set was written.</param>
public sealed record SpecArtifactSet(
    string Slug,
    string RootPath,
    string RequirementsPath,
    string DesignPath,
    string TasksPath,
    DateTimeOffset GeneratedAtUtc);
