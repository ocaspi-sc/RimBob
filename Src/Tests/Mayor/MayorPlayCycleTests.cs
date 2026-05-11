using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class MayorPlayCycleTests
{
    [Fact]
    public async Task FirstCycle_StoresAgendaV1AndPublishesEvent()
    {
        Harness h = new((_, _, _, _, _) => CannedAgenda("first"));

        await h.Mayor.RunPlayCycle(CancellationToken.None);

        h.Store.Current.Should().NotBeNull();
        h.Store.Current!.Version.Should().Be(1);
        h.Store.Current.UpdateNotes.Should().Be("first");
        h.PublishedAgendas.Should().ContainSingle().Which.Version.Should().Be(1);
    }

    [Fact]
    public async Task SecondCycle_IncrementsVersionAndPushesPreviousIntoHistory()
    {
        int call = 0;
        Harness h = new((_, _, _, _, _) => CannedAgenda($"v{++call}"));

        await h.Mayor.RunPlayCycle(CancellationToken.None);
        await h.Mayor.RunPlayCycle(CancellationToken.None);

        h.Store.Current!.Version.Should().Be(2);
        h.Store.Current.UpdateNotes.Should().Be("v2");
        h.Store.History.Should().ContainSingle().Which.UpdateNotes.Should().Be("v1");
        h.PublishedAgendas.Should().HaveCount(2);
    }

    [Fact]
    public async Task ShortTermOverCap_OnFirstCallTriggersRetry_SecondCallTruncates()
    {
        int call = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            call++;
            // Both attempts return 6 items; Mayor truncates after the second attempt.
            return Task.FromResult(InputBuilder.Default with
            {
                UpdateNotes = $"call{call}",
                ShortTerm   = SixActiveBullets()
            });
        });

        await h.Mayor.RunPlayCycle(CancellationToken.None);

        call.Should().Be(2);
        h.Store.Current.Should().NotBeNull();
        h.Store.Current!.ShortTerm.Should().HaveCount(5);
    }

    [Fact]
    public async Task LlmAlwaysThrows_AgendaUntouched_NoPublish()
    {
        Harness h = new((_, _, _, _, _) => throw new InvalidOperationException("boom"));

        await h.Mayor.RunPlayCycle(CancellationToken.None);

        h.Store.Current.Should().BeNull();
        h.PublishedAgendas.Should().BeEmpty();
    }

    private static Task<MayorAgendaInput> CannedAgenda(string note) =>
        Task.FromResult(InputBuilder.Default with { UpdateNotes = note });

    private static List<AgendaPriority> SixActiveBullets() => Enumerable.Range(1, 6)
        .Select(i => new AgendaPriority($"st_{i}", $"Bullet {i}", AgendaPriorityStatus.Active))
        .ToList();

    private sealed class Harness
    {
        public ColonyState     Colony   { get; }
        public BriefingCache   Cache    { get; }
        public AgendaStore     Store    { get; }
        public AdviceBus       Bus      { get; }
        public MayorMinister   Mayor    { get; }
        public List<MayorAgenda> PublishedAgendas { get; } = [];

        public Harness(LlmClient.MayorCallExecutor executor)
        {
            Colony = new();
            Cache  = new(Colony, new TestLogger<BriefingCache>());
            Store  = new();
            Bus    = new();
            Bus.AgendaUpdated += e => PublishedAgendas.Add(e.Agenda);

            LlmClient      llm       = new(NullLogger<LlmClient>.Instance, executor);
            KnowledgeBase  kb        = new();
            MayorRagRetriever retriever = new(kb, embedder: null, enabled: false, topK: 0,
                                           NullLogger<MayorRagRetriever>.Instance);
            Mayor = new(Cache, new MayorAgendaRules(), Store, llm, new PromptBuilder(), Bus, new MayorStatus(),
                        retriever, NullLogger<MayorMinister>.Instance);
        }
    }
}
