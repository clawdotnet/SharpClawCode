using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Uses evaluated compile items, then includes consuming projects and their tests.</summary>
public sealed class AffectedProjectResolver : IAffectedProjectResolver
{
    /// <inheritdoc />
    public AffectedProjectSelection Resolve(DotNetSolutionSummary solution, IReadOnlyList<string>? changedPaths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        AffectedProjectSelection All() => new(solution.Projects.Select(project => project.Path).Order(comparer).ToArray(), true);
        if (changedPaths is null || changedPaths.Count == 0 || solution.Status != DotNetWorkspaceStatus.Ready) return All();
        var selected = new HashSet<string>(comparer);
        foreach (var changed in changedPaths)
        {
            var path = changed.Replace('\\', '/');
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return All();
            var owners = solution.Projects.Where(project => project.SourcePaths?.Contains(path, comparer) == true).ToArray();
            // Deleted, linked, excluded or unknown items never imply an empty successful verification.
            if (owners.Length == 0) return All();
            foreach (var owner in owners) selected.Add(owner.Path);
        }
        bool added;
        do
        {
            added = false;
            foreach (var edge in solution.ProjectReferences) if (selected.Contains(edge.ReferencedProjectPath)) added |= selected.Add(edge.ProjectPath);
        } while (added);
        return new(selected.Order(comparer).ToArray(), false);
    }
}
