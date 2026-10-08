using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>Loads approved compiler workspaces lazily; no semantic load is involved in ordinary indexed search.</summary>
public sealed partial class DotNetWorkspaceSemanticService : IDotNetWorkspaceSemanticService, IDisposable
{
    private readonly IFileSystem fileSystem;
    private readonly IPathService pathService;
    private readonly DotNetSemanticWorkspaceCache cache;

    /// <summary>Creates a semantic service with a bounded, lifecycle-owned cache.</summary>
    public DotNetWorkspaceSemanticService(IFileSystem fileSystem, IPathService pathService, IDotNetWorkspaceTargetResolver targetResolver)
    {
        this.fileSystem = fileSystem;
        this.pathService = pathService;
        cache = new DotNetSemanticWorkspaceCache(fileSystem, pathService, targetResolver);
    }

    /// <inheritdoc />
    public async Task<DotNetSolutionSummary> InspectSolutionAsync(DotNetWorkspaceRequest request, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        using var lease = await cache.AcquireAsync(request, authorization, cancellationToken).ConfigureAwait(false);
        var target = lease.Target;
        if (lease.Snapshot is not { } snapshot)
        {
            return new DotNetSolutionSummary(target.Status, target.WorkspaceRoot, target.TargetPath is null ? null : Relative(target.WorkspaceRoot, target.TargetPath), [], [], target.Message is null ? [] : [target.Message]);
        }

        var projects = new List<DotNetProjectSummary>();
        foreach (var project in snapshot.Solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            var projectPath = Relative(target.WorkspaceRoot, project.FilePath!);
            var xml = await ReadProjectXmlAsync(project.FilePath!, cancellationToken).ConfigureAwait(false);
            var packages = xml.Descendants().Where(element => element.Name.LocalName == "PackageReference")
                .Where(element => element.Attribute("Include") is not null)
                .Select(element => new DotNetPackageReference(element.Attribute("Include")!.Value, element.Attribute("Version")?.Value ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value)).ToArray();
            var frameworks = xml.Descendants().Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
            var assetsPath = Path.Combine(Path.GetDirectoryName(project.FilePath!)!, "obj", "project.assets.json");
            if (await fileSystem.ReadAllTextIfExistsAsync(assetsPath, cancellationToken).ConfigureAwait(false) is { } assetsText)
            {
                using var assets = JsonDocument.Parse(assetsText);
                if (assets.RootElement.TryGetProperty("project", out var projectNode) && projectNode.TryGetProperty("frameworks", out var frameworkNodes)) frameworks = frameworkNodes.EnumerateObject().Select(property => property.Name).ToArray();
                if (assets.RootElement.TryGetProperty("libraries", out var libraries))
                {
                    packages = packages.Select(package => package with
                    {
                        Version = libraries.EnumerateObject().Where(property => property.Name.StartsWith(package.Name + "/", StringComparison.OrdinalIgnoreCase)).Select(property => property.Name[(package.Name.Length + 1)..]).FirstOrDefault() ?? package.Version
                    }).ToArray();
                }
            }

            var references = project.ProjectReferences.Select(reference => snapshot.Solution.GetProject(reference.ProjectId)?.FilePath)
                .OfType<string>().Select(path => Relative(target.WorkspaceRoot, path)).Distinct().Order(StringComparer.Ordinal).ToArray();
            var evaluatedFramework = project.OutputFilePath is null ? request.TargetFramework : Path.GetFileName(Path.GetDirectoryName(project.OutputFilePath));
            var isTestProject = xml.Descendants().Any(element => element.Name.LocalName == "IsTestProject" && string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase))
                || packages.Any(package => package.Name is "Microsoft.NET.Test.Sdk" or "xunit" or "NUnit" or "MSTest.TestFramework")
                || project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
            projects.Add(new DotNetProjectSummary(project.Name, projectPath, project.Language, frameworks, evaluatedFramework, isTestProject, project.DocumentIds.Count, references, packages, project.Documents.Where(document => document.FilePath is not null).Select(document => Relative(target.WorkspaceRoot, document.FilePath!)).Distinct().ToArray(), project.OutputFilePath is null ? null : Relative(target.WorkspaceRoot, project.OutputFilePath)));
        }

        return new DotNetSolutionSummary(snapshot.Status, target.WorkspaceRoot, Relative(target.WorkspaceRoot, target.TargetPath!), projects.ToArray(),
            projects.SelectMany(project => project.ProjectReferences.Select(reference => new DotNetProjectReference(project.Path, reference))).ToArray(),
            [.. snapshot.Messages, "Project-supplied analyzers and source generators are excluded from compiler queries."]);
    }

    /// <inheritdoc />
    public async Task<CSharpSymbolResolutionResult> ResolveSymbolAsync(DotNetWorkspaceRequest request, CSharpSymbolQuery query, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Name);
        using var lease = await cache.AcquireAsync(request, authorization, cancellationToken).ConfigureAwait(false);
        if (lease.Snapshot is not { } snapshot)
        {
            return new CSharpSymbolResolutionResult(CSharpSymbolResolutionStatus.WorkspaceUnavailable, lease.Target.Status, [], Messages: lease.Target.Message is null ? [] : [lease.Target.Message]);
        }

        var resolved = await ResolveCandidatesAsync(snapshot.Solution, lease.Target.WorkspaceRoot, query, cancellationToken).ConfigureAwait(false);
        var candidates = resolved.Select(candidate => Describe(candidate.Symbol, candidate.Project, lease.Target.WorkspaceRoot)).ToArray();
        return new CSharpSymbolResolutionResult(candidates.Length switch
        {
            0 => CSharpSymbolResolutionStatus.NotFound,
            1 => CSharpSymbolResolutionStatus.Resolved,
            _ => CSharpSymbolResolutionStatus.Ambiguous
        }, snapshot.Status, candidates, candidates.Length == 1 ? candidates[0] : null, snapshot.Messages);
    }

    private async Task<XDocument> ReadProjectXmlAsync(string path, CancellationToken cancellationToken)
    {
        var text = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Project file disappeared.");
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    internal async Task<List<(ISymbol Symbol, Project Project)>> ResolveCandidatesAsync(Solution solution, string root, CSharpSymbolQuery query, CancellationToken cancellationToken)
    {
        string? selectedPath = null;
        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            selectedPath = pathService.GetCanonicalFullPath(Path.IsPathRooted(query.Path) ? query.Path : Path.Combine(root, query.Path));
            if (!DotNetWorkspaceTargetResolver.IsWithin(root, selectedPath)) throw new ArgumentException("Symbol path escapes the workspace boundary.", nameof(query));
        }
        var result = new List<(ISymbol, Project)>();
        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp).OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            if (query.Project is not null && !string.Equals(query.Project, project.Name, StringComparison.Ordinal) && !string.Equals(query.Project.Replace('\\', '/'), Relative(root, project.FilePath!), StringComparison.Ordinal)) continue;
            var symbols = await SymbolFinder.FindSourceDeclarationsAsync(project, query.Name, ignoreCase: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var symbol in symbols.Where(symbol => string.Equals(symbol.Name, query.Name, StringComparison.Ordinal)))
            {
                if (query.Kind is not null && !string.Equals(query.Kind, Kind(symbol), StringComparison.OrdinalIgnoreCase)) continue;
                if (query.Container is not null && !string.Equals(query.Container, symbol.ContainingSymbol?.ToDisplayString(), StringComparison.Ordinal)) continue;
                if (query.Signature is not null && !string.Equals(query.Signature, symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparison.Ordinal)) continue;
                var locations = symbol.Locations.Where(location => location.IsInSource && location.SourceTree?.FilePath is not null).ToArray();
                if (!locations.Any(location =>
                {
                    var path = pathService.GetCanonicalFullPath(location.SourceTree!.FilePath);
                    var span = location.GetLineSpan();
                    return DotNetWorkspaceTargetResolver.IsWithin(root, path)
                        && (selectedPath is null || string.Equals(selectedPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        && (query.Line is null || query.Line == span.StartLinePosition.Line + 1)
                        && (query.Column is null || query.Column == span.StartLinePosition.Character + 1);
                })) continue;
                if (!result.Any(candidate => candidate.Item2.Id == project.Id && SymbolEqualityComparer.Default.Equals(candidate.Item1, symbol))) result.Add((symbol, project));
            }
        }
        return result;
    }

    internal static CSharpSymbolDescriptor Describe(ISymbol symbol, Project project, string root)
        => new(symbol.Name, Kind(symbol), symbol.ContainingSymbol?.ToDisplayString() ?? "", symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), symbol.GetDocumentationCommentId(), Relative(root, project.FilePath!),
            symbol.Locations.Where(location => location.IsInSource && location.SourceTree is not null && DotNetWorkspaceTargetResolver.IsWithin(root, location.SourceTree.FilePath)).Select(location => DescribeLocation(location, root)).Distinct().OrderBy(location => location.Path, StringComparer.Ordinal).ThenBy(location => location.Line).ToArray());

    internal static CSharpSymbolLocation DescribeLocation(Location location, string root)
    {
        var span = location.GetLineSpan();
        return new CSharpSymbolLocation(Relative(root, span.Path), span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
    }

    private static string Kind(ISymbol symbol) => symbol is INamedTypeSymbol type ? type.IsRecord ? "record" : type.TypeKind.ToString().ToLowerInvariant() : symbol.Kind.ToString().ToLowerInvariant();
    private static string Relative(string root, string path) => DotNetWorkspaceTargetResolver.Relative(root, path);

    /// <inheritdoc />
    public void Dispose() => cache.Dispose();
}
