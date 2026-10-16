using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>Provides on-demand compiler semantics alongside the existing persisted lexical index.</summary>
public interface IDotNetWorkspaceSemanticService
{
    /// <summary>Inspects an approved solution/project; null authorization permits valid cached queries only.</summary>
    Task<DotNetSolutionSummary> InspectSolutionAsync(DotNetWorkspaceRequest request, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);

    /// <summary>Resolves exact source symbols, returning candidates instead of guessing.</summary>
    Task<CSharpSymbolResolutionResult> ResolveSymbolAsync(DotNetWorkspaceRequest request, CSharpSymbolQuery query, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);
    /// <summary>Finds compiler references to an exactly selected symbol.</summary>
    Task<CSharpReferenceResult> FindReferencesAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);
    /// <summary>Finds the selected type's semantic relationships.</summary>
    Task<CSharpTypeHierarchyResult> GetTypeHierarchyAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);
    /// <summary>Gets compiler diagnostics with bounded paging and filters.</summary>
    Task<CSharpDiagnosticsResult> GetDiagnosticsAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);
    /// <summary>Calculates a conflict-checked rename without applying changes to disk.</summary>
    Task<CSharpRenamePlan> PlanRenameAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken);
}
