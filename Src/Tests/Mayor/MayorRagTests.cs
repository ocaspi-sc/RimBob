using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Knowledge;
using RimAI.LLM;
using MayorMinister = RimAI.Ministers.Mayor.Mayor;
using RimAI.Ministers.Mayor;
using RimAI.State;
using RimAI.Tests.Coordination;
using RimAI.Tests.Infrastructure;

namespace RimAI.Tests.Mayor;

/// <summary>
/// M2 RAG side-by-side: same briefing run with an empty KnowledgeBase vs. a
/// preloaded one. Citations should appear only when retrieval has something to
/// return.
/// </summary>
public sealed class MayorRagTests
{
    [Fact]
    public void RagVsNoRagFixture_CapturesCitationDiff()
    {
        JsonObject noRag = ReadFixture("agenda-no-rag.json");
        JsonObject withRag = ReadFixture("agenda-with-rag.json");

        noRag["citations"]!.AsArray().Should().BeEmpty();
        withRag["citations"]!.AsArray().Should().NotBeEmpty();
        withRag["short_term"]![0]!["cite_ids"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .Should().Contain("g1");
    }

    [Fact]
    public async Task NoRag_AgendaCitationsEmpty_PromptHasNoRetrievedGuides()
    {
        Harness h = new(retriever: DisabledRetriever());

        await h.Mayor.RunPlayCycle(CancellationToken.None);

        h.Store.Current.Should().NotBeNull();
        h.Store.Current!.Citations.Should().BeEmpty();
        h.LastRetrievedGuides.Should().BeEmpty();
    }

    [Fact]
    public async Task WithRag_AgendaCarriesCitationsAndPromptIncludesGuides()
    {
        ChunkMetadata meta = new("guides/strategic-plan-y1-y2.md", "Winter prep", 12, 18);
        Chunk c1 = new("c1", "Build a 60-day food buffer before winter.", meta, [1f, 0f, 0f]);
        Chunk c2 = new("c2", "Stone all wooden buildings by Q2.", meta, [0.9f, 0.1f, 0f]);
        Chunk c3 = new("c3", "Distractor: trade caravan logistics.", meta, [0f, 1f, 0f]);

        KnowledgeBase  kb        = new([c1, c2, c3]);
        StaticEmbedder embedder  = new([1f, 0f, 0f]);  // aligns with c1/c2
        MayorRetriever retriever = new(kb, embedder, enabled: true, topK: 2,
                                       NullLogger<MayorRetriever>.Instance);

        Harness h = new(retriever);

        await h.Mayor.RunPlayCycle(CancellationToken.None);

        h.Store.Current.Should().NotBeNull();
        h.Store.Current!.Citations.Should().HaveCount(2);
        h.Store.Current.Citations[0].CiteId.Should().Be("g1");
        h.Store.Current.Citations[0].Snippet.Should().Contain("60-day food buffer");

        h.LastRetrievedGuides.Should().HaveCount(2);
        h.LastRetrievedGuides.Select(c => c.SourcePath)
            .Should().AllBe("guides/strategic-plan-y1-y2.md");
    }

    private static MayorRetriever DisabledRetriever()
        => new(new KnowledgeBase(), embedder: null, enabled: false, topK: 0,
               NullLogger<MayorRetriever>.Instance);

    private static JsonObject ReadFixture(string fileName)
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "Src", "Tests", "Mayor", "Fixtures", "rag-vs-norag", fileName);
            if (File.Exists(candidate))
            {
                string json = File.ReadAllText(candidate);
                return JsonNode.Parse(json)!.AsObject();
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find RAG fixture {fileName}.");
    }

    private sealed class StaticEmbedder : IEmbedder
    {
        private readonly float[] _vec;
        public StaticEmbedder(float[] vec) { _vec = vec; }
        public Task<float[]> EmbedAsync(string text, CancellationToken ct) => Task.FromResult(_vec);
    }

    private sealed class Harness
    {
        public ColonyState        Colony { get; }
        public BriefingCache      Cache  { get; }
        public AgendaStore        Store  { get; }
        public AdviceBus          Bus    { get; }
        public MayorMinister      Mayor  { get; }
        public IReadOnlyList<Citation> LastRetrievedGuides { get; private set; } = [];

        public Harness(MayorRetriever retriever)
        {
            Colony = new();
            Cache  = new(Colony, new TestLogger<BriefingCache>());
            Store  = new();
            Bus    = new();

            LlmClient.MayorCallExecutor executor = (_, _, _, retrieved, _) =>
            {
                LastRetrievedGuides = retrieved;
                return Task.FromResult(InputBuilder.Default with { UpdateNotes = "rag-test" });
            };
            LlmClient llm = new(NullLogger<LlmClient>.Instance, executor);

            Mayor = new(Cache, new MayorRules(), Store, llm, new PromptBuilder(), Bus,
                        new MayorStatus(), retriever, NullLogger<MayorMinister>.Instance);
        }
    }
}
