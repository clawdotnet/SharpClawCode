using FluentAssertions;
using SharpClaw.Code.Memory.Services;

namespace SharpClaw.Code.UnitTests.MemorySkillsGit;

/// <summary>
/// Covers deterministic local embedding behavior.
/// </summary>
public sealed class HashTextEmbeddingServiceTests
{
    [Fact]
    public void Embed_should_be_deterministic_for_the_same_input()
    {
        var first = HashTextEmbeddingService.Embed("Widget prompts should stay concise.");
        var second = HashTextEmbeddingService.Embed("Widget prompts should stay concise.");

        first.Should().Equal(second);
    }

    [Fact]
    public void Cosine_should_prefer_related_content()
    {
        var query = HashTextEmbeddingService.Embed("concise widget prompt");
        var related = HashTextEmbeddingService.Embed("Widget prompts should stay concise and repo specific.");
        var unrelated = HashTextEmbeddingService.Embed("database migration and sql schema");

        HashTextEmbeddingService.Cosine(query, related).Should().BeGreaterThan(HashTextEmbeddingService.Cosine(query, unrelated));
    }
}
