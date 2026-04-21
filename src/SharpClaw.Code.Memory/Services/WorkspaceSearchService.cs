using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Executes hybrid search across lexical chunk matches, symbols, and deterministic semantic similarity.
/// </summary>
public sealed class WorkspaceSearchService(IWorkspaceKnowledgeStore knowledgeStore) : IWorkspaceSearchService
{
    /// <inheritdoc />
    public async Task<WorkspaceSearchResult> SearchAsync(
        string workspaceRoot,
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(request);

        var limit = Math.Clamp(request.Limit.GetValueOrDefault(8), 1, 50);
        var lexical = await knowledgeStore
            .SearchChunksLexicalAsync(workspaceRoot, request.Query, limit * 2, cancellationToken)
            .ConfigureAwait(false);
        var symbols = request.IncludeSymbols
            ? await knowledgeStore.SearchSymbolsAsync(workspaceRoot, request.Query, limit * 2, cancellationToken).ConfigureAwait(false)
            : [];
        var semantic = request.IncludeSemantic
            ? await ComputeSemanticHitsAsync(workspaceRoot, request.Query, limit * 2, cancellationToken).ConfigureAwait(false)
            : [];
        var status = await knowledgeStore.GetWorkspaceIndexStatusAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);

        var merged = lexical
            .Concat(symbols)
            .Concat(semantic)
            .GroupBy(static hit => $"{hit.Kind}:{hit.Path}:{hit.StartLine}:{hit.EndLine}:{hit.SymbolName}", StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static hit => hit.Score).First())
            .OrderByDescending(static hit => hit.Score)
            .Take(limit)
            .ToArray();

        return new WorkspaceSearchResult(
            Query: request.Query,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            IndexRefreshedAtUtc: status.RefreshedAtUtc,
            Hits: merged);
    }

    private async Task<IReadOnlyList<WorkspaceSearchHit>> ComputeSemanticHitsAsync(
        string workspaceRoot,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var chunks = await knowledgeStore.ListChunksAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
        {
            return [];
        }

        var queryVector = HashTextEmbeddingService.Embed(query);
        return chunks
            .Select(chunk => new WorkspaceSearchHit(
                Path: chunk.Path,
                Kind: WorkspaceSearchHitKind.Semantic,
                Score: HashTextEmbeddingService.Cosine(queryVector, chunk.Embedding),
                Excerpt: chunk.Excerpt,
                SymbolName: null,
                SymbolKind: null,
                StartLine: chunk.StartLine,
                EndLine: chunk.EndLine))
            .Where(static hit => hit.Score > 0)
            .OrderByDescending(static hit => hit.Score)
            .Take(limit)
            .ToArray();
    }
}
