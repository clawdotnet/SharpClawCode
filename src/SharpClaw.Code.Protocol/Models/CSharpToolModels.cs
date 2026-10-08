namespace SharpClaw.Code.Protocol.Models;

/// <summary>Portable semantic tool input. Host context supplies the workspace and evaluation authorization.</summary>
public sealed record CSharpToolArguments(
    string? Target = null, string Configuration = "Debug", string? TargetFramework = null,
    string? Name = null, string? Container = null, string? Path = null, string? Kind = null,
    string? Project = null, string? Signature = null, int? Line = null, int? Column = null,
    string? Severity = null, string? Code = null, int Offset = 0, int Limit = 50, string? NewName = null)
{
    /// <summary>Creates the compilation context within a host-owned boundary.</summary>
    public DotNetWorkspaceRequest Workspace(string root) => new(root, Target, Configuration, TargetFramework);
    /// <summary>Creates exact symbol selectors.</summary>
    public CSharpSymbolQuery Query() => new(Name ?? throw new ArgumentException("name is required."), Container, Path, Kind, Project, Signature, Line, Column);
}

/// <summary>A compiler-resolved use site in one project context.</summary>
public sealed record CSharpReference(string ProjectPath, CSharpSymbolLocation Location, string Kind);
/// <summary>Reports paged references and exact resolution without guessing.</summary>
public sealed record CSharpReferenceResult(CSharpSymbolResolutionResult Resolution, CSharpReference[] References, int Total, int Offset, bool Truncated);
/// <summary>Reports a type's base, interfaces, derived types and implementations.</summary>
public sealed record CSharpTypeHierarchyResult(CSharpSymbolResolutionResult Resolution, CSharpSymbolDescriptor? BaseType, CSharpSymbolDescriptor[] Interfaces, CSharpSymbolDescriptor[] DerivedTypes, CSharpSymbolDescriptor[] Implementations, bool Truncated);
/// <summary>A compiler diagnostic with source and compilation attribution.</summary>
public sealed record CSharpDiagnostic(string ProjectPath, string? TargetFramework, string Code, string Severity, string Message, CSharpSymbolLocation? Location);
/// <summary>Reports paged compiler diagnostics without running project analyzers or generators.</summary>
public sealed record CSharpDiagnosticsResult(DotNetWorkspaceStatus Status, CSharpDiagnostic[] Diagnostics, int Total, int Offset, bool Truncated, string[] Messages);
