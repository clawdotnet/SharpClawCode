using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Builds a workspace knowledge index from source files, symbols, and project references.
/// </summary>
public sealed class WorkspaceIndexService(
    IFileSystem fileSystem,
    IPathService pathService,
    IWorkspaceKnowledgeStore knowledgeStore) : IWorkspaceIndexService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".sharpclaw",
        "bin",
        "obj",
        "node_modules"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".md", ".txt", ".json", ".jsonc", ".yml", ".yaml", ".xml", ".props", ".targets", ".config", ".editorconfig", ".ts", ".tsx", ".js", ".jsx", ".sh", ".ps1", ".cmd"
    };

    /// <inheritdoc />
    public async Task<WorkspaceIndexRefreshResult> RefreshAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var normalizedRoot = pathService.GetFullPath(workspaceRoot);
        var files = EnumerateCandidateFiles(normalizedRoot).ToArray();
        var chunks = new List<IndexedWorkspaceChunk>();
        var symbols = new List<IndexedWorkspaceSymbol>();
        var edges = new List<IndexedWorkspaceProjectEdge>();
        var skipped = new List<string>();

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTextCandidate(filePath))
            {
                skipped.Add(Relativize(normalizedRoot, filePath));
                continue;
            }

            var content = await fileSystem.ReadAllTextIfExistsAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var relativePath = Relativize(normalizedRoot, filePath);
            var language = DetectLanguage(filePath);
            chunks.AddRange(CreateChunks(relativePath, content, language));

            if (language == "csharp")
            {
                symbols.AddRange(ExtractCSharpSymbols(relativePath, content));
            }
            else if (filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                edges.AddRange(ExtractProjectEdges(relativePath, content));
            }
        }

        var refreshedAt = DateTimeOffset.UtcNow;
        await knowledgeStore.ReplaceWorkspaceIndexAsync(
            normalizedRoot,
            new WorkspaceIndexDocument(chunks, symbols, edges),
            refreshedAt,
            cancellationToken).ConfigureAwait(false);

        return new WorkspaceIndexRefreshResult(
            WorkspaceRoot: normalizedRoot,
            RefreshedAtUtc: refreshedAt,
            IndexedFileCount: files.Length - skipped.Count,
            ChunkCount: chunks.Count,
            SymbolCount: symbols.Count,
            ProjectEdgeCount: edges.Count,
            SkippedPaths: skipped.ToArray());
    }

    /// <inheritdoc />
    public Task<WorkspaceIndexStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
        => knowledgeStore.GetWorkspaceIndexStatusAsync(workspaceRoot, cancellationToken);

    private IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in fileSystem.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (IgnoredDirectories.Contains(name))
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in fileSystem.EnumerateFiles(current, "*"))
            {
                yield return file;
            }
        }
    }

    private static bool IsTextCandidate(string filePath)
        => TextExtensions.Contains(Path.GetExtension(filePath));

    private static string DetectLanguage(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".md" => "markdown",
            ".json" or ".jsonc" => "json",
            ".xml" or ".csproj" or ".props" or ".targets" => "xml",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".yml" or ".yaml" => "yaml",
            _ => "text"
        };

    private static IReadOnlyList<IndexedWorkspaceChunk> CreateChunks(string relativePath, string content, string language)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        const int chunkSize = 40;
        var results = new List<IndexedWorkspaceChunk>();
        for (var index = 0; index < lines.Length; index += chunkSize)
        {
            var startLine = index + 1;
            var endLine = Math.Min(lines.Length, index + chunkSize);
            var chunkText = string.Join(Environment.NewLine, lines[index..endLine]).Trim();
            if (string.IsNullOrWhiteSpace(chunkText))
            {
                continue;
            }

            var excerpt = chunkText.Length <= 240 ? chunkText : chunkText[..240].TrimEnd() + "...";
            var id = $"{relativePath}:{startLine}-{endLine}";
            results.Add(new IndexedWorkspaceChunk(
                Id: id,
                Path: relativePath,
                Language: language,
                Excerpt: excerpt,
                Content: chunkText,
                StartLine: startLine,
                EndLine: endLine,
                Embedding: HashTextEmbeddingService.Embed(chunkText)));
        }

        return results;
    }

    private static IReadOnlyList<IndexedWorkspaceSymbol> ExtractCSharpSymbols(string relativePath, string content)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(content);
        var root = syntaxTree.GetRoot();
        var results = new List<IndexedWorkspaceSymbol>();
        var walker = new SymbolCollector(relativePath, root.SyntaxTree, results);
        walker.Visit(root);
        return results;
    }

    private static IReadOnlyList<IndexedWorkspaceProjectEdge> ExtractProjectEdges(string relativePath, string content)
    {
        try
        {
            var document = XDocument.Parse(content);
            var edges = new List<IndexedWorkspaceProjectEdge>();
            foreach (var projectReference in document.Descendants().Where(static element => element.Name.LocalName == "ProjectReference"))
            {
                if (projectReference.Attribute("Include")?.Value is { Length: > 0 } include)
                {
                    edges.Add(new IndexedWorkspaceProjectEdge(relativePath, include, "project-reference"));
                }
            }

            foreach (var packageReference in document.Descendants().Where(static element => element.Name.LocalName == "PackageReference"))
            {
                if (packageReference.Attribute("Include")?.Value is { Length: > 0 } include)
                {
                    edges.Add(new IndexedWorkspaceProjectEdge(relativePath, include, "package-reference"));
                }
            }

            return edges;
        }
        catch
        {
            return [];
        }
    }

    private static string Relativize(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private sealed class SymbolCollector(string relativePath, SyntaxTree syntaxTree, List<IndexedWorkspaceSymbol> symbols) : CSharpSyntaxWalker
    {
        private readonly Stack<string> containers = new();

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            containers.Push(node.Name.ToString());
            base.VisitNamespaceDeclaration(node);
            containers.Pop();
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            containers.Push(node.Name.ToString());
            base.VisitFileScopedNamespaceDeclaration(node);
            containers.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "class", node.Identifier.GetLocation());
            VisitContainer(node.Identifier.Text, () => base.VisitClassDeclaration(node));
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "record", node.Identifier.GetLocation());
            VisitContainer(node.Identifier.Text, () => base.VisitRecordDeclaration(node));
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "struct", node.Identifier.GetLocation());
            VisitContainer(node.Identifier.Text, () => base.VisitStructDeclaration(node));
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "interface", node.Identifier.GetLocation());
            VisitContainer(node.Identifier.Text, () => base.VisitInterfaceDeclaration(node));
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "enum", node.Identifier.GetLocation());
            VisitContainer(node.Identifier.Text, () => base.VisitEnumDeclaration(node));
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "method", node.Identifier.GetLocation());
            base.VisitMethodDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            Add(node.Identifier.Text, "property", node.Identifier.GetLocation());
            base.VisitPropertyDeclaration(node);
        }

        private void VisitContainer(string name, Action inner)
        {
            containers.Push(name);
            inner();
            containers.Pop();
        }

        private void Add(string name, string kind, Location location)
        {
            var lineSpan = syntaxTree.GetLineSpan(location.SourceSpan);
            var container = containers.Count == 0 ? null : string.Join('.', containers.Reverse());
            symbols.Add(new IndexedWorkspaceSymbol(
                Id: $"{relativePath}:{kind}:{name}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}",
                Path: relativePath,
                Name: name,
                Kind: kind,
                Container: container,
                Line: lineSpan.StartLinePosition.Line + 1,
                Column: lineSpan.StartLinePosition.Character + 1));
        }
    }
}
