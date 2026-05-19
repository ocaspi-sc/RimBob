using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using MayorMinister = RimBob.Ministers.Mayor.Mayor;
using RimBob.Ministers.Mayor;
using RimBob.State;
using RimBob.Tests.Coordination;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Mayor;

public sealed class MayorPlayCycleTests
{
    [Fact]
    public async Task FirstCycle_StoresAgendaV1AndPublishesEvent()
    {
        Harness h = new((_, _, _, _, _) => CannedAgenda("first"));

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.Version.Should().Be(1);
        h.Store.CurrentMayorAgenda.UpdateNotes.Should().Be("first");
        h.PublishedAgendas.Should().ContainSingle().Which.Version.Should().Be(1);
    }

    [Fact]
    public async Task FirstCycle_PersistsMayorLlmReplayRecord()
    {
        Harness h = new((_, _, _, _, _) => CannedAgenda("first"));

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        MinisterReplayRecord record = h.Replay.Records.Should().ContainSingle(r => r.Path == "llm").Subject;
        record.SchemaVersion.Should().Be(2);
        record.Minister.Should().Be("Mayor");
        record.Trigger.Should().Be(nameof(PlayCycleTrigger.StartupBootstrap));
        record.EscalationReason.Should().Be("mayor_agenda_update");
        record.Context.Should().NotBeNull();
        record.GuideCitations.Should().BeEmpty();
        record.OutputKind.Should().Be("mayor_agenda_input");
        record.Output.Should().BeOfType<MayorAgendaInput>()
            .Which.UpdateNotes.Should().Be("first");
    }

    [Fact]
    public async Task SecondCycle_IncrementsSnapshotVersion()
    {
        int call = 0;
        Harness h = new((_, _, _, _, _) => CannedAgenda($"v{++call}"));

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        await h.Mayor.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.Store.CurrentMayorAgenda!.Version.Should().Be(2);
        h.Store.CurrentMayorAgenda.UpdateNotes.Should().Be("v2");
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

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        call.Should().Be(2);
        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.ShortTerm.Should().HaveCount(5);
        h.Replay.Records.Should().ContainSingle(r =>
            r.Path == "llm_failed" && r.Error != null && r.Error.Type == "ValidationRejected");
        h.Replay.Records.Should().ContainSingle(r =>
            r.Path == "llm" && r.OutputKind == "mayor_agenda_input");
    }

    [Fact]
    public async Task LlmAlwaysThrows_WithNoExistingAgenda_PublishesBootstrapAgenda()
    {
        Harness h = new((_, _, _, _, _) => throw new InvalidOperationException("boom"));

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.Version.Should().Be(1);
        h.Store.CurrentMayorAgenda.UpdateNotes.Should().Contain("LLM failed twice");
        h.Store.CurrentMayorAgenda.ShortTerm.Should().Contain(item => item.Id == "bootstrap_replace_with_mayor_run");
        h.PublishedAgendas.Should().ContainSingle().Which.Version.Should().Be(1);
        h.Replay.Records.Where(r => r.Path == "llm_failed")
            .Should().HaveCount(2)
            .And.AllSatisfy(r => r.Error!.Type.Should().Be(nameof(InvalidOperationException)));
    }

    [Fact]
    public async Task LlmAlwaysThrows_WithExistingAgenda_KeepsCurrentAgendaAndDoesNotPublish()
    {
        Harness h = new((_, _, _, _, _) => throw new InvalidOperationException("boom"));
        await h.Store.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "existing" }, "tick0");

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.UpdateNotes.Should().Be("existing");
        h.PublishedAgendas.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualResponseFile_PersistsReplayRecordWithoutCallingProvider()
    {
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            return CannedAgenda("provider");
        });
        WriteManualResponse(h, InputBuilder.Default with { UpdateNotes = "manual" });

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        calls.Should().Be(0);
        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.UpdateNotes.Should().Be("manual");
        MinisterReplayRecord record = h.Replay.Records.Should()
            .ContainSingle(r => r.Path == "llm" && r.EscalationReason == "manual_llm_response_file")
            .Subject;
        record.Llm.Should().NotBeNull();
        record.Llm!.Provider.Should().Be("ManualFile");
        record.Llm.Status.Should().Be("manual_parsed");
        record.OutputKind.Should().Be("mayor_agenda_input");
    }

    [Fact]
    public async Task ManualResponseFileParseFailure_PersistsFailureThenFallsThroughToProvider()
    {
        Harness h = new((_, _, _, _, _) => CannedAgenda("fallback"));
        Directory.CreateDirectory(Path.GetDirectoryName(h.ManualResponsePath)!);
        File.WriteAllText(h.ManualResponsePath, "{");

        await h.Mayor.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.Store.CurrentMayorAgenda.Should().NotBeNull();
        h.Store.CurrentMayorAgenda!.UpdateNotes.Should().Be("fallback");
        h.Replay.Records.Should().ContainSingle(r =>
            r.Path == "llm_failed" && r.EscalationReason == "manual_llm_response_file");
        h.Replay.Records.Should().ContainSingle(r =>
            r.Path == "llm" && r.EscalationReason == "mayor_agenda_update");
    }

    private static Task<MayorAgendaInput> CannedAgenda(string note) =>
        Task.FromResult(InputBuilder.Default with { UpdateNotes = note });

    private static void WriteManualResponse(Harness h, MayorAgendaInput input)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(h.ManualResponsePath)!);
        File.WriteAllText(h.ManualResponsePath, JsonSerializer.Serialize(input));
    }

    private static List<AgendaPriority> SixActiveBullets() => Enumerable.Range(1, 6)
        .Select(i => new AgendaPriority($"st_{i}", $"Bullet {i}", AgendaPriorityStatus.Active))
        .ToList();

    private sealed class Harness
    {
        public ColonyState     Colony   { get; }
        public BriefingCache   Cache    { get; }
        public MinisterOutputStore Store { get; }
        public AdviceBus       Bus      { get; }
        public MayorMinister   Mayor    { get; }
        public CapturingReplayWriter Replay { get; } = new();
        public string ManualResponsePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "rimbob-mayor-manual-response-tests",
            Guid.NewGuid().ToString("N"),
            "mayor-response.json");
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
                        retriever, new FlagChannel(), NullLogger<MayorMinister>.Instance,
                        new MinisterReplayRecorder(Replay), ManualResponsePath);
        }
    }

    private sealed class CapturingReplayWriter : IReplayCorpusWriter
    {
        public List<MinisterReplayRecord> Records { get; } = [];

        public Task WriteAsync(MinisterReplayRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
