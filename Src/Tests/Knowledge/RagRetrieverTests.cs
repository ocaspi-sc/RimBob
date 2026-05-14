using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Knowledge;
using RimAI.State;
using RimAI.Tests.Food;
using RimAI.Tests.Infrastructure;

namespace RimAI.Tests.Knowledge;

public sealed class RagRetrieverTests
{
    [Fact]
    public async Task FoodRetriever_UsesFoodProfileCitationPrefixAndSnippetSuffix()
    {
        string longSnippet = string.Join(' ', Enumerable.Repeat("food-storage-buffer", 40));
        ChunkMetadata metadata = new("guides/food.md", "Freezer policy", 1, 10);
        Chunk chunk = new("food-1", longSnippet, metadata, [1f, 0f]);
        KnowledgeBase kb = new([chunk]);
        StaticEmbedder embedder = new([1f, 0f]);
        FoodRagRetriever retriever = new(kb, embedder, enabled: true, topK: 1, NullLogger<FoodRagRetriever>.Instance);

        IReadOnlyList<GuideCitation> citations = await retriever.RetrieveAsync(FoodRulesTests.Briefing(12f), CancellationToken.None);

        GuideCitation citation = citations.Should().ContainSingle().Subject;
        citation.CiteId.Should().Be("fg1");
        citation.Snippet.Should().EndWith("...");
    }

    [Fact]
    public void StaticBuildQueryEntryPoints_MatchMinisterQueryBuilders()
    {
        MayorBriefing mayorBriefing = new BriefingCache(new ColonyState(), new TestLogger<BriefingCache>()).GetMayorBriefing();

        MayorRagRetriever.BuildQuery(mayorBriefing, ["secure food"])
            .Should().Be(MayorRagQueryBuilder.Build(mayorBriefing, ["secure food"]));
        FoodRagRetriever.BuildQuery(FoodRulesTests.Briefing(12f))
            .Should().Be(FoodRagQueryBuilder.Build(FoodRulesTests.Briefing(12f)));
    }

    private sealed class StaticEmbedder : IEmbedder
    {
        private readonly float[] _embedding;

        public StaticEmbedder(float[] embedding)
        {
            _embedding = embedding;
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct) => Task.FromResult(_embedding);
    }
}
