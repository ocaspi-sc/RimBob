using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.Ministers.Food;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Food;

public sealed class FoodMinisterTests
{
    [Fact]
    public async Task FirstCycle_StableState_BootstrapsThroughLlm()
    {
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new FoodLlmResponse([], []));
        });
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        calls.Should().Be(1);
        h.PublishedAdvice.Should().BeEmpty();
    }

    [Fact]
    public async Task SecondCycle_StableState_UsesRulesWithoutLlm()
    {
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new FoodLlmResponse([], []));
        });
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        calls.Should().Be(1);
        h.PublishedAdvice.Should().BeEmpty();
    }

    [Fact]
    public async Task BootstrapFailure_FallsBackToRules()
    {
        Harness h = new((_, _, _, _, _) => throw new InvalidOperationException("boom"));
        h.SetFoodDays(4f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.Concern.Should().Be("food_security");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task RuleDecision_PublishesAdviceAndFlag_AfterBootstrap()
    {
        Harness h = new((_, _, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])));
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.Concern.Should().Be("food_security");
        h.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Food");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task RuleDecision_PersistsReplayRecord_AfterBootstrap()
    {
        CapturingReplayWriter replay = new();
        Harness h = new((_, _, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])), replay);
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        MinisterReplayRecord bootstrap = replay.Records.Single(r => r.EscalationReason == "bootstrap_first_live_cycle");
        bootstrap.RuleTraceDetails.Should().NotBeNull();
        bootstrap.RuleTraceDetails!.SelectedRule.Should().Be("bootstrap_first_live_cycle");
        bootstrap.RuleTraceDetails.MatchedSignals.Should().ContainSingle(signal =>
            signal.Rule == "bootstrap_first_live_cycle" &&
            signal.Outcome == "escalated");
        replay.Records.Should().Contain(r => r.Path == "rules" && r.RuleTrace == "emergency_food_flag");
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "rules" && r.RuleTrace == "emergency_food_flag");
        record.SchemaVersion.Should().Be(2);
        record.Minister.Should().Be("Food");
        record.Trigger.Should().Be(nameof(PlayCycleTrigger.ManualTrigger));
        record.WakeupPayload.Should().Be("dashboard");
        record.Briefing.Should().BeOfType<FoodBriefing>();
        record.Context.Should().BeOfType<MinisterBriefingContext>();
        record.Advice.Should().ContainSingle().Which.Concern.Should().Be("food_security");
        record.Flags.Should().ContainSingle().Which.Domain.Should().Be("food");
        record.Chain.Should().NotBeNull();
        record.Chain!.Paths.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RuleDecision_ReplacesBootstrapAdviceSnapshot()
    {
        Harness h = new((_, _, _, _, _) => Task.FromResult(new FoodLlmResponse(
            [FoodAdvice("bootstrap_1"), FoodAdvice("bootstrap_2")],
            [])));
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.Bus.ActiveAdvice().Select(a => a.Id).Should().BeEquivalentTo(["bootstrap_1", "bootstrap_2"]);

        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.Bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("food_emergency_food_flag");
    }

    [Fact]
    public async Task Escalation_UsesFoodLlmResponse_AfterBootstrap()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        List<IReadOnlyList<FoodPromptCropCandidate>> candidateCalls = [];
        Harness h = new((_, _, _, cropCandidates, _) =>
        {
            candidateCalls.Add(cropCandidates);
            calls++;
            return Task.FromResult(calls == 1
                ? new FoodLlmResponse([], [])
                : new FoodLlmResponse(
                    "Food is below target and hunting may be viable.",
                    [FoodAdvice("llm_food", withAction: true)],
                    [new AgentFlag("food:llm", "Food", FlagSeverity.Medium, "food", "LLM food flag")]));
        }, replay);
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(25f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.Id.Should().Be("llm_food");
        candidateCalls.Should().HaveCount(2);
        candidateCalls[1].Should().Contain(candidate => candidate.CropDef == "Plant_Rice");
        candidateCalls[1].Should().Contain(candidate => candidate.CropDef == "Plant_Corn");
        IReadOnlyDictionary<string, string> stateSummaries = h.Bus.ActiveSnapshot().StateSummaries!;
        stateSummaries.Should().ContainKey("Food")
            .WhoseValue.Should().Contain("Stores:");
        h.Bus.ActiveSnapshot().Chains.Should().ContainKey("Food");
        stateSummaries["Food"].Should().Contain("25.0 days");
        stateSummaries["Food"].Should().NotBe("Food is below target and hunting may be viable.");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Summary.Should().Be("LLM food flag");
        MinisterReplayRecord llmRecord = replay.Records.Single(r =>
            r.Path == "llm" &&
            r.Advice.Any(advice => advice.Id == "llm_food"));
        llmRecord.RuleTraceDetails.Should().NotBeNull();
        llmRecord.RuleTraceDetails!.SelectedRule.Should().Be("winter_food_tradeoff");
        llmRecord.RuleTraceDetails.EmittedActions.Should().ContainSingle(row =>
            row.Source == "llm_after_escalation" &&
            row.Rule == "winter_food_tradeoff" &&
            row.AdviceId == "llm_food" &&
            row.Kind == AdviceActionKind.MarkHunt);
        llmRecord.RuleTraceDetails.EmittedFlags.Should().ContainSingle(row =>
            row.Source == "llm_after_escalation" &&
            row.Rule == "winter_food_tradeoff" &&
            row.FlagId == "food:llm");
    }

    [Fact]
    public async Task EscalationFailure_PersistsFailedReplayRecord()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(new FoodLlmResponse([], []));
            throw new InvalidOperationException("quota exhausted");
        }, replay);
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(12f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        replay.Records.Should().Contain(r => r.Path == "llm_failed");
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "llm_failed");
        record.EscalationReason.Should().Contain("hunting path");
        record.Error.Should().NotBeNull();
        record.Error!.Type.Should().Be(nameof(InvalidOperationException));
        record.Error.Message.Should().Be("quota exhausted");
        record.SchemaVersion.Should().Be(2);
        record.Advice.Should().BeEmpty();
        record.Flags.Should().BeEmpty();
    }

    private static AdviceItem FoodAdvice(string id, bool withAction = false) => new(
        Id: id,
        Minister: "Food",
        Concern: "hunt_for_food",
        Priority: AdvicePriority.Medium,
        Title: "Hunt carefully",
        Body: "Use safe targets.",
        Rationale: "LLM selected hunting path.",
        Actions: withAction
            ? [new AdviceAction(
                AdviceActionKind.MarkHunt,
                "Mark a small safe hunting batch.")]
            : [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(4));

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public BriefingCache Cache { get; }
        public AdviceBus Bus { get; } = new();
        public FlagChannel Flags { get; } = new();
        public MinisterOfFood Minister { get; }
        public List<AdviceItem> PublishedAdvice { get; } = [];

        public Harness(LlmClient.FoodCallExecutor executor, IReplayCorpusWriter? replay = null)
        {
            Cache = new(Colony, new TestLogger<BriefingCache>());
            Bus.AdvicePublished += PublishedAdvice.Add;
            LlmClient llm = new(NullLogger<LlmClient>.Instance, executor);
            FoodRagRetriever retriever = new(new KnowledgeBase(), null, false, 0, NullLogger<FoodRagRetriever>.Instance);
            MinisterReplayRecorder? replayRecorder = replay is null ? null : new MinisterReplayRecorder(replay);
            Minister = new(Cache, new Rules(), new MinisterOutputStore(), Bus, Flags, llm, retriever,
                NullLogger<MinisterOfFood>.Instance, replayRecorder);
        }

        public void SetFoodDays(float days, int wildAnimals = 0, string dateTimeRaw = "5th of Aprimay, 5500, 14h", string animalDef = "Hare")
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = DateTimeOffset.UtcNow;
            Colony.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, dateTimeRaw));
            Colony.Colonists.Update(new ColonistRegistry([
                new ColonistRecord("p1", "P1", 30, "Female", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p2", "P2", 30, "Female", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p3", "P3", 30, "Female", 1f, 0.7f, 1f, false, false, null, null, [], [])
            ]));
            Colony.Resources.Update(new ResourceSummary(100, 0f, 80, days * FoodNutrition.NutritionPerColonistPerDay * 3, 20, 20, 0, 0, 0f));
            Colony.Buildings.Update(new BuildingRegistry([new BuildingRecord("cooler1", "Cooler", 1f, null, null)]));
            Colony.Animals.Update(new AnimalRegistry(Enumerable.Range(0, wildAnimals)
                .Select(i => new AnimalRecord($"a{i}", animalDef, false, 1f))
                .ToList()));
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
