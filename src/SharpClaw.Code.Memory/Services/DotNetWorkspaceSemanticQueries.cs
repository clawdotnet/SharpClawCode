using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

public sealed partial class DotNetWorkspaceSemanticService
{
    /// <inheritdoc />
    public async Task<CSharpReferenceResult> FindReferencesAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        using var lease = await cache.AcquireAsync(request, authorization, cancellationToken).ConfigureAwait(false);
        var (resolution, candidates) = await ResolveAsync(lease, arguments.Query(), cancellationToken).ConfigureAwait(false);
        if (resolution.Symbol is null) return new(resolution, [], 0, arguments.Offset, false);
        var groups = await SymbolFinder.FindReferencesAsync(candidates[0].Symbol, lease.Snapshot!.Solution, cancellationToken).ConfigureAwait(false);
        var references = groups.SelectMany(group => group.Locations)
            .Where(reference => reference.Document.FilePath is not null && DotNetWorkspaceTargetResolver.IsWithin(lease.Target.WorkspaceRoot, pathService.GetCanonicalFullPath(reference.Document.FilePath)))
            .Select(reference => new CSharpReference(Relative(lease.Target.WorkspaceRoot, reference.Document.Project.FilePath!), DescribeLocation(reference.Location, lease.Target.WorkspaceRoot), reference.IsImplicit ? "implicit" : "explicit"))
            .Distinct().OrderBy(reference => reference.Location.Path, StringComparer.Ordinal).ThenBy(reference => reference.Location.Line).ThenBy(reference => reference.Location.Column).ToArray();
        var (offset, limit) = Page(arguments);
        return new(resolution, references.Skip(offset).Take(limit).ToArray(), references.Length, offset, offset + limit < references.Length);
    }

    /// <inheritdoc />
    public async Task<CSharpTypeHierarchyResult> GetTypeHierarchyAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        using var lease = await cache.AcquireAsync(request, authorization, cancellationToken).ConfigureAwait(false);
        var (resolution, candidates) = await ResolveAsync(lease, arguments.Query(), cancellationToken).ConfigureAwait(false);
        if (resolution.Symbol is null) return new(resolution, null, [], [], [], false);
        if (candidates[0].Symbol is not INamedTypeSymbol type) throw new ArgumentException("Hierarchy requires a class, interface, struct or record.");
        var solution = lease.Snapshot!.Solution;
        var project = candidates[0].Project;
        var root = lease.Target.WorkspaceRoot;
        var derived = type.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindDerivedInterfacesAsync(type, solution, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await SymbolFinder.FindDerivedClassesAsync(type, solution, cancellationToken: cancellationToken).ConfigureAwait(false);
        var implementations = type.TypeKind == TypeKind.Interface
            ? await SymbolFinder.FindImplementationsAsync(type, solution, cancellationToken: cancellationToken).ConfigureAwait(false) : [];
        CSharpSymbolDescriptor DescribeType(ISymbol symbol) => Describe(symbol, symbol.Locations.Where(location => location.SourceTree is not null).Select(location => solution.GetDocument(location.SourceTree!)?.Project).FirstOrDefault(item => item is not null) ?? project, root);
        var (_, limit) = Page(arguments);
        var derivedArray = derived.Select(DescribeType).OrderBy(item => item.Signature, StringComparer.Ordinal).ToArray();
        var implementationArray = implementations.Select(DescribeType).Distinct().OrderBy(item => item.Signature, StringComparer.Ordinal).ToArray();
        return new(resolution, type.BaseType is null ? null : DescribeType(type.BaseType), type.AllInterfaces.Select(DescribeType).Take(limit).ToArray(), derivedArray.Take(limit).ToArray(), implementationArray.Take(limit).ToArray(), type.AllInterfaces.Length > limit || derivedArray.Length > limit || implementationArray.Length > limit);
    }

    /// <inheritdoc />
    public async Task<CSharpDiagnosticsResult> GetDiagnosticsAsync(DotNetWorkspaceRequest request, CSharpToolArguments arguments, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        using var lease = await cache.AcquireAsync(request, authorization, cancellationToken).ConfigureAwait(false);
        var (offset, limit) = Page(arguments);
        if (lease.Snapshot is not { } snapshot) return new(lease.Target.Status, [], 0, offset, false, lease.Target.Message is null ? [] : [lease.Target.Message]);
        var diagnostics = new List<CSharpDiagnostic>();
        foreach (var project in snapshot.Solution.Projects)
        {
            var relativeProject = Relative(lease.Target.WorkspaceRoot, project.FilePath!);
            if (arguments.Project is not null && arguments.Project != project.Name && arguments.Project != relativeProject) continue;
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null) continue;
            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                if (arguments.Severity is not null && !string.Equals(arguments.Severity, diagnostic.Severity.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                if (arguments.Code is not null && !string.Equals(arguments.Code, diagnostic.Id, StringComparison.OrdinalIgnoreCase)) continue;
                CSharpSymbolLocation? location = null;
                if (diagnostic.Location.IsInSource && diagnostic.Location.SourceTree is not null)
                {
                    if (!DotNetWorkspaceTargetResolver.IsWithin(lease.Target.WorkspaceRoot, pathService.GetCanonicalFullPath(diagnostic.Location.SourceTree.FilePath))) continue;
                    location = DescribeLocation(diagnostic.Location, lease.Target.WorkspaceRoot);
                }
                if (arguments.Path is not null && arguments.Path.Replace('\\', '/') != location?.Path) continue;
                diagnostics.Add(new(relativeProject, project.OutputFilePath is null ? null : Path.GetFileName(Path.GetDirectoryName(project.OutputFilePath)), diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(), diagnostic.GetMessage(), location));
            }
        }
        var ordered = diagnostics.Distinct().OrderBy(item => item.ProjectPath, StringComparer.Ordinal).ThenBy(item => item.Location?.Path, StringComparer.Ordinal).ThenBy(item => item.Location?.Line).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
        return new(snapshot.Status, ordered.Skip(offset).Take(limit).ToArray(), ordered.Length, offset, offset + limit < ordered.Length, snapshot.Messages);
    }

    private async Task<(CSharpSymbolResolutionResult Resolution, List<(ISymbol Symbol, Project Project)> Candidates)> ResolveAsync(DotNetSemanticWorkspaceCache.Lease lease, CSharpSymbolQuery query, CancellationToken cancellationToken)
    {
        if (lease.Snapshot is null) return (new(CSharpSymbolResolutionStatus.WorkspaceUnavailable, lease.Target.Status, [], Messages: lease.Target.Message is null ? [] : [lease.Target.Message]), []);
        var candidates = await ResolveCandidatesAsync(lease.Snapshot.Solution, lease.Target.WorkspaceRoot, query, cancellationToken).ConfigureAwait(false);
        var descriptions = candidates.Select(item => Describe(item.Symbol, item.Project, lease.Target.WorkspaceRoot)).ToArray();
        return (new(descriptions.Length switch { 0 => CSharpSymbolResolutionStatus.NotFound, 1 => CSharpSymbolResolutionStatus.Resolved, _ => CSharpSymbolResolutionStatus.Ambiguous }, lease.Target.Status, descriptions, descriptions.Length == 1 ? descriptions[0] : null), candidates);
    }

    private static (int Offset, int Limit) Page(CSharpToolArguments arguments)
    {
        if (arguments.Offset < 0 || arguments.Limit is < 1 or > 200) throw new ArgumentException("offset must be nonnegative and limit must be between 1 and 200.");
        return (arguments.Offset, arguments.Limit);
    }
}
