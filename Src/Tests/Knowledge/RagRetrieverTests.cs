using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Knowledge;
using RimBob.State;
using RimBob.Tests.Food;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Knowledge;

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
    public async Task WelfareRetriever_UsesWelfareProfileCitationPrefix()
    {
        ChunkMetadata metadata = new("guides/welfare.md", "Mood pressure", 1, 10);
        Chunk chunk = new("welfare-1", "mood recreation room impressiveness social pressure", metadata, [1f, 0f]);
        KnowledgeBase kb = new([chunk]);
        StaticEmbedder embedder = new([1f, 0f]);
        WelfareRagRetriever retriever = new(kb, embedder, enabled: true, topK: 1, NullLogger<WelfareRagRetriever>.Instance);

        IReadOnlyList<GuideCitation> citations = await retriever.RetrieveAsync(WelfareBriefing(), CancellationToken.None);

        citations.Should().ContainSingle()
            .Which.CiteId.Should().Be("wg1");
    }

    [Fact]
    public void StaticBuildQueryEntryPoints_MatchMinisterQueryBuilders()
    {
        MayorBriefing mayorBriefing = new BriefingCache(new ColonyState(), new TestLogger<BriefingCache>()).GetMayorBriefing();

        MayorRagRetriever.BuildQuery(mayorBriefing, ["secure food"])
            .Should().Be(MayorRagQueryBuilder.Build(mayorBriefing, ["secure food"]));
        FoodRagRetriever.BuildQuery(FoodRulesTests.Briefing(12f))
            .Should().Be(FoodRagQueryBuilder.Build(FoodRulesTests.Briefing(12f)));
        WelfareRagRetriever.BuildQuery(WelfareBriefing())
            .Should().Be(WelfareRagQueryBuilder.Build(WelfareBriefing()));
    }

    private static WelfareSourceBriefing WelfareBriefing() => new(
        BriefingVersion: 7,
        GameTick: 300_000,
        ColonistCount: 3,
        Mood: new WelfareMoodSummary(0.58f, BreakRiskCount: 0, StressedCount: 1, ContentCount: 1),
        WorstPawns: [],
        NeedLows: [],
        Rooms: new WelfareRoomSummary(1, 1, 0, 30f, []),
        Sleep: new WelfareSleepSummary(3, 3, 0, 0),
        Recreation: new WelfareRecreationSummary(0, 0, 0, false),
        ThoughtDigest: new WelfareThoughtDigest(
        [
            new WelfareThoughtGroup(ThoughtCategory.Social, 1, -5f, "social fight")
        ]),
        DataCoverage: new WelfareDataCoverage(true, true, true, true, false));

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
