using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Recalls the highest-signal structured memory entries for a prompt.
/// </summary>
public sealed class MemoryRecallService(IPersistentMemoryStore persistentMemoryStore) : IMemoryRecallService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(
        string workspaceRoot,
        string prompt,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidateCount = Math.Max(limit * 3, limit);
        var entries = await persistentMemoryStore
            .ListAsync(workspaceRoot, scope: null, prompt, Math.Max(limit * 3, limit), cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            entries = await persistentMemoryStore
                .ListAsync(workspaceRoot, scope: null, query: null, Math.Max(candidateCount, 50), cancellationToken)
                .ConfigureAwait(false);
        }

        if (entries.Count == 0)
        {
            return [];
        }

        var queryVector = HashTextEmbeddingService.Embed(prompt);
        var promptTokens = Tokenize(prompt);
        return entries
            .Select(entry => new
            {
                Entry = entry,
                Score = Score(entry, queryVector, prompt, promptTokens),
            })
            .OrderByDescending(static item => item.Score)
            .ThenByDescending(static item => item.Entry.UpdatedAtUtc)
            .Take(limit)
            .Select(static item => item.Entry)
            .ToArray();
    }

    private static double Score(MemoryEntry entry, float[] queryVector, string prompt, HashSet<string> promptTokens)
    {
        var lexical = entry.Content.Contains(prompt, StringComparison.OrdinalIgnoreCase) ? 1d : 0d;
        if (lexical == 0d && promptTokens.Count > 0)
        {
            var entryTokens = Tokenize(entry.Content);
            lexical = promptTokens.Intersect(entryTokens, StringComparer.OrdinalIgnoreCase).Count() / (double)promptTokens.Count;
        }

        var semantic = HashTextEmbeddingService.Cosine(queryVector, HashTextEmbeddingService.Embed(entry.Content));
        return semantic * 0.75d + lexical * 0.25d;
    }

    private static HashSet<string> Tokenize(string text)
        => text
            .Split([' ', '\r', '\n', '\t', '.', ',', ':', ';', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => token.Trim().ToLowerInvariant())
            .Where(static token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
