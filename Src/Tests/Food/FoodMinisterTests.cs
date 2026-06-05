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

        h.PublishedAdvice.Should().Contain(advice => advice.Id == "chef_emergency_food_flag");
        h.Flags.Active(Priority.Medium).Should().Contain(flag => flag.Domain == "food");
    }

    [Fact]
    public async Task RuleDecision_PublishesAdviceAndFlag_AfterBootstrap()
    {
        Harness h = new((_, _, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])));
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.PublishedAdvice.Should().Contain(advice => advice.Id == "chef_emergency_food_flag");
        h.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Chef");
        h.Flags.Active(Priority.Medium).Should().Contain(flag => flag.Domain == "food");
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

        replay.Records.Should().Contain(r => r.Path == "rules" && (r.RuleTrace ?? "").Contains("emergency_food_flag", StringComparison.OrdinalIgnoreCase));
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "rules" && (r.RuleTrace ?? "").Contains("emergency_food_flag", StringComparison.OrdinalIgnoreCase));
        record.SchemaVersion.Should().Be(2);
        record.Minister.Should().Be("Chef");
        record.Trigger.Should().Be(nameof(PlayCycleTrigger.ManualTrigger));
        record.WakeupPayload.Should().Be("dashboard");
        record.Briefing.Should().BeOfType<FoodBriefing>();
        record.Context.Should().BeOfType<MinisterBriefingContext>();
        record.Advice.Should().Contain(advice => advice.Id == "chef_emergency_food_flag");
        record.Flags.Should().Contain(flag => flag.Domain == "food");
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

        h.Bus.ActiveAdvice().Should().Contain(advice => advice.Id == "chef_emergency_food_flag");
    }

    [Fact]
    public async Task ManualRulesOnly_WhenRulesEscalate_DoesNotCallLlm()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new FoodLlmResponse([FoodAdvice("llm_food")], []));
        }, replay);
        h.SetFoodDays(25f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");

        await h.Minister.RunPlayCycle(PlayCycleContext.ManualRulesOnly, CancellationToken.None);

        calls.Should().Be(0);
        h.PublishedAdvice.Should().BeEmpty();
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "rules");
        record.WakeupPayload.Should().Be("dashboard:rules");
        record.EscalationReason.Should().NotBeNullOrWhiteSpace();
        record.Llm.Should().BeNull();
    }

    [Fact]
    public async Task ManualForceLlm_CallsLlmEvenWhenRulesWouldDecide()
    {
        int calls = 0;
        Harness h = new((_, _, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new FoodLlmResponse([FoodAdvice("forced_llm_food")], []));
        });
        h.SetFoodDays(4f);

        await h.Minister.RunPlayCycle(PlayCycleContext.ManualForceLlm, CancellationToken.None);

        calls.Should().Be(1);
        h.PublishedAdvice.Should().ContainSingle().Which.Id.Should().Be("forced_llm_food");
    }

    [Fact]
    public async Task RulesFirst_WhenRulesEscalate_AwaitsDashboardConfirmation()
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
                    [new AgentFlag("food:llm", "Chef", Priority.Medium, "food", "LLM food flag")]));
        }, replay);
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(25f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        calls.Should().Be(1);
        h.PublishedAdvice.Should().BeEmpty();
        candidateCalls.Should().ContainSingle();
        IReadOnlyDictionary<string, string> stateSummaries = h.Bus.ActiveSnapshot().StateSummaries!;
        stateSummaries.Should().ContainKey("Chef")
            .WhoseValue.Should().Contain("Stores:");
        h.Bus.ActiveSnapshot().Chains.Should().ContainKey("Chef");
        stateSummaries["Chef"].Should().Contain("25.0 days");
        h.Flags.Active(Priority.Medium).Should().BeEmpty();
        MinisterReplayRecord pendingRecord = replay.Records.Single(r =>
            r.Path == "rules" &&
            !string.IsNullOrWhiteSpace(r.EscalationReason));
        pendingRecord.RuleTraceDetails.Should().NotBeNull();
        pendingRecord.RuleTraceDetails!.SelectedRule!.Value.Value.Should().Be("winter_food_tradeoff");
        pendingRecord.RuleTraceDetails.AllRules.Should().ContainSingle(row =>
            row.Rule == "winter_food_tradeoff" &&
            row.Outcome == RuleOutcome.Escalated);
        pendingRecord.Llm.Should().BeNull();
    }

    [Fact]
    public async Task ManualForceLlmFailure_PersistsFailedReplayRecord()
    {
        CapturingReplayWriter replay = new();
        Harness h = new((_, _, _, _, _) => throw new InvalidOperationException("quota exhausted"), replay);
        h.SetFoodDays(12f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");
        await h.Minister.RunPlayCycle(PlayCycleContext.ManualForceLlm, CancellationToken.None);

        replay.Records.Should().Contain(r => r.Path == "llm_failed");
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "llm_failed");
        record.WakeupPayload.Should().Be("dashboard:llm");
        record.EscalationReason.Should().Contain("dashboard Run LLM");
        record.Error.Should().NotBeNull();
        record.Error!.Type.Should().Be(nameof(InvalidOperationException));
        record.Error.Message.Should().Be("quota exhausted");
        record.SchemaVersion.Should().Be(2);
        record.Advice.Should().BeEmpty();
        record.Flags.Should().BeEmpty();
    }

    private static AdviceItem FoodAdvice(string id, bool withAction = false) => new(
        Id: id,
        Minister: "Chef",
        Priority: Priority.Medium,
        Title: "Hunt carefully",
        Body: "Use safe targets.",
        Rationale: "LLM selected hunting path.",
        Actions: withAction
            ? [new AdviceAction(
                AdviceActionKind.MarkHunt,
                "Mark a small safe hunting batch.")]
            : [],
        GuideCitationIds: [],
        Stamp: new AdviceStamp(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(4)));

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public BriefingCache Cache { get; }
        public AdviceBus Bus { get; } = new();
        public FlagChannel Flags { get; } = new();
        public Chef Minister { get; }
        public List<AdviceItem> PublishedAdvice { get; } = [];

        public Harness(LlmClient.FoodCallExecutor executor, IReplayCorpusWriter? replay = null)
        {
            Cache = new(Colony, new TestLogger<BriefingCache>());
            Bus.AdvicePublished += PublishedAdvice.Add;
            LlmClient llm = new(NullLogger<LlmClient>.Instance, executor);
            FoodRagRetriever retriever = new(new KnowledgeBase(), null, false, 0, NullLogger<FoodRagRetriever>.Instance);
            MinisterReplayRecorder? replayRecorder = replay is null ? null : new MinisterReplayRecorder(replay);
            Minister = new(Cache, new Rules(), new MinisterOutputStore(), Bus, Flags, llm, retriever,
                NullLogger<Chef>.Instance, replayRecorder);
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
