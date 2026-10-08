namespace SharpClaw.Code.Protocol.Models;
/// <summary>Nullable configuration fields support user/workspace precedence; omitted verification remains disabled.</summary>
public sealed record VerificationOptions(bool? Enabled = null, VerificationScope? Scope = null, bool? RunAfterMutatingTurn = null, int? MaxRepairIterations = null, bool? AllowRestore = null);
/// <summary>Validated verification behavior for one logical turn.</summary>
public sealed record VerificationPolicy(bool Enabled = false, VerificationScope Scope = VerificationScope.Affected, bool RunAfterMutatingTurn = true, int MaxRepairIterations = 2, bool AllowRestore = false);
/// <summary>A correlated provider request and its observed events from one agent pass.</summary>
public sealed record ProviderInvocationRecord(ProviderRequest Request, ProviderEvent[] Events);

/// <summary>Bounded structured failure input for an agent repair pass.</summary>
public sealed record VerificationRepairContext(VerificationStatus Status, VerificationFailureReason Reason, VerificationScope Scope, string? Target, string[] AffectedProjects, VerificationDiagnostic[] Diagnostics, VerificationTestCaseResult[] Tests, string Summary);
