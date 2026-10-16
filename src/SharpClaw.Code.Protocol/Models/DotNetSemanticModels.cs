namespace SharpClaw.Code.Protocol.Models;

/// <summary>Identifies the outcome of resolving or loading a .NET workspace.</summary>
public enum DotNetWorkspaceStatus
{
    /// <summary>The workspace is available.</summary>
    Ready,
    /// <summary>Several plausible targets require an explicit selection.</summary>
    AmbiguousTarget,
    /// <summary>The workspace has no supported target.</summary>
    NoTarget,
    /// <summary>The target escapes the workspace or is unsupported.</summary>
    InvalidTarget,
    /// <summary>Project evaluation was not authorized.</summary>
    PermissionDenied,
    /// <summary>Required SDK or build tools are unavailable.</summary>
    SdkUnavailable,
    /// <summary>Explicit package restore is required.</summary>
    RestoreRequired,
    /// <summary>Project loading failed.</summary>
    LoadFailed,
    /// <summary>Some projects or references could not be loaded.</summary>
    Partial
}

/// <summary>Selects a semantic workspace and its compilation context. Contains no execution authorization.</summary>
/// <param name="WorkspaceRoot">Workspace boundary.</param>
/// <param name="Target">Optional solution or project path within the workspace.</param>
/// <param name="Configuration">MSBuild configuration.</param>
/// <param name="TargetFramework">Optional framework for multi-targeted projects.</param>
public sealed record DotNetWorkspaceRequest(string WorkspaceRoot, string? Target = null, string Configuration = "Debug", string? TargetFramework = null);

/// <summary>Describes deterministic, non-executing target discovery.</summary>
/// <param name="Status">Discovery outcome.</param>
/// <param name="WorkspaceRoot">Canonical workspace root.</param>
/// <param name="TargetPath">Selected canonical path, if any.</param>
/// <param name="Candidates">Workspace-relative candidates when ambiguous.</param>
/// <param name="Message">Explanation of unsuccessful discovery.</param>
public sealed record DotNetWorkspaceTarget(DotNetWorkspaceStatus Status, string WorkspaceRoot, string? TargetPath, string[] Candidates, string? Message = null);

/// <summary>Describes a evaluated package reference.</summary>
/// <param name="Name">Package identifier.</param>
/// <param name="Version">Resolved version when available.</param>
public sealed record DotNetPackageReference(string Name, string? Version);

/// <summary>Describes a project dependency edge.</summary>
/// <param name="ProjectPath">Referencing project.</param>
/// <param name="ReferencedProjectPath">Referenced project.</param>
public sealed record DotNetProjectReference(string ProjectPath, string ReferencedProjectPath);

/// <summary>Describes a loaded project without exposing compiler types.</summary>
/// <param name="Name">Project name.</param>
/// <param name="Path">Workspace-relative project path.</param>
/// <param name="Language">Compiler language.</param>
/// <param name="TargetFrameworks">Declared/restored target frameworks.</param>
/// <param name="EvaluatedTargetFramework">Framework of this compilation.</param>
/// <param name="IsTestProject">Test-project heuristic.</param>
/// <param name="OutputPath">Evaluated output assembly path, relative to the workspace when contained.</param>
/// <param name="SourcePaths">Evaluated source paths used for affected ownership.</param>
/// <param name="DocumentCount">Source document count.</param>
/// <param name="ProjectReferences">Evaluated project references.</param>
/// <param name="PackageReferences">Declared packages and available resolved versions.</param>
public sealed record DotNetProjectSummary(string Name, string Path, string Language, string[] TargetFrameworks, string? EvaluatedTargetFramework, bool IsTestProject, int DocumentCount, string[] ProjectReferences, DotNetPackageReference[] PackageReferences, string[]? SourcePaths = null, string? OutputPath = null);

/// <summary>Describes the solution/project graph and load limitations.</summary>
/// <param name="Status">Load outcome.</param>
/// <param name="WorkspaceRoot">Canonical root.</param>
/// <param name="TargetPath">Workspace-relative selected target.</param>
/// <param name="Projects">Loaded projects.</param>
/// <param name="ProjectReferences">Evaluated dependency edges.</param>
/// <param name="Messages">Load diagnostics and limitations.</param>
public sealed record DotNetSolutionSummary(DotNetWorkspaceStatus Status, string WorkspaceRoot, string? TargetPath, DotNetProjectSummary[] Projects, DotNetProjectReference[] ProjectReferences, string[] Messages);

/// <summary>Locates an exact source symbol; extra selectors disambiguate overloads and projects.</summary>
/// <param name="Name">Exact symbol name.</param>
/// <param name="Container">Optional containing namespace/type.</param>
/// <param name="Path">Optional declaration path.</param>
/// <param name="Kind">Optional class/interface/record/method/property/etc. kind.</param>
/// <param name="Project">Optional project name or path.</param>
/// <param name="Signature">Optional fully qualified display signature.</param>
/// <param name="Line">Optional one-based declaration line.</param>
/// <param name="Column">Optional one-based declaration column.</param>
public sealed record CSharpSymbolQuery(string Name, string? Container = null, string? Path = null, string? Kind = null, string? Project = null, string? Signature = null, int? Line = null, int? Column = null);

/// <summary>Describes a one-based source location.</summary>
/// <param name="Path">Workspace-relative source path.</param>
/// <param name="Line">One-based line.</param>
/// <param name="Column">One-based column.</param>
/// <param name="EndLine">One-based end line.</param>
/// <param name="EndColumn">One-based end column.</param>
public sealed record CSharpSymbolLocation(string Path, int Line, int Column, int EndLine, int EndColumn);

/// <summary>Describes an exact compiler symbol in one project context.</summary>
/// <param name="Name">Symbol name.</param>
/// <param name="Kind">Portable symbol kind.</param>
/// <param name="Container">Containing namespace/type.</param>
/// <param name="Signature">Fully qualified display signature.</param>
/// <param name="DocumentationId">Compiler documentation identifier, when available.</param>
/// <param name="ProjectPath">Workspace-relative defining project.</param>
/// <param name="Locations">Source declarations.</param>
public sealed record CSharpSymbolDescriptor(string Name, string Kind, string Container, string Signature, string? DocumentationId, string ProjectPath, CSharpSymbolLocation[] Locations);

/// <summary>Identifies exact symbol lookup outcomes.</summary>
public enum CSharpSymbolResolutionStatus
{
    /// <summary>One exact source symbol remains.</summary>
    Resolved,
    /// <summary>Several candidates require more selectors.</summary>
    Ambiguous,
    /// <summary>No matching source symbol exists.</summary>
    NotFound,
    /// <summary>Workspace discovery/loading did not succeed.</summary>
    WorkspaceUnavailable
}

/// <summary>Reports an exact lookup without guessing when ambiguous.</summary>
/// <param name="Status">Lookup outcome.</param>
/// <param name="WorkspaceStatus">Workspace load outcome.</param>
/// <param name="Candidates">Matching source symbols.</param>
/// <param name="Symbol">Selected symbol when exactly one matches.</param>
/// <param name="Messages">Load/lookup explanations.</param>
public sealed record CSharpSymbolResolutionResult(CSharpSymbolResolutionStatus Status, DotNetWorkspaceStatus WorkspaceStatus, CSharpSymbolDescriptor[] Candidates, CSharpSymbolDescriptor? Symbol = null, string[]? Messages = null);
