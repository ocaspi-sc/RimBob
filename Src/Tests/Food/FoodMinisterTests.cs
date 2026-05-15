using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.Ministers.Food;
using RimAI.State;
using RimAI.Tests.Infrastructure;

namespace RimAI.Tests.Food;

public sealed class FoodMinisterTests
{
    [Fact]
    public async Task FirstCycle_StableState_BootstrapsThroughLlm()
    {
        int calls = 0;
        Harness h = new((_, _, _, _) =>
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
        Harness h = new((_, _, _, _) =>
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
        Harness h = new((_, _, _, _) => throw new InvalidOperationException("boom"));
        h.SetFoodDays(4f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.AdviceType.Should().Be("food_security");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task RuleDecision_PublishesAdviceAndFlag_AfterBootstrap()
    {
        Harness h = new((_, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])));
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.AdviceType.Should().Be("food_security");
        h.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Food");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task RuleDecision_PersistsReplayRecord_AfterBootstrap()
    {
        CapturingReplayWriter replay = new();
        Harness h = new((_, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])), replay);
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(4f);
        await h.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        replay.Records.Should().Contain(r => r.Path == "rules" && r.RuleTrace == "emergency_food_flag");
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "rules" && r.RuleTrace == "emergency_food_flag");
        record.SchemaVersion.Should().Be(2);
        record.Minister.Should().Be("Food");
        record.Trigger.Should().Be(nameof(PlayCycleTrigger.ManualTrigger));
        record.WakeupPayload.Should().Be("dashboard");
        record.Briefing.Should().BeOfType<FoodBriefing>();
        record.Context.Should().BeOfType<MinisterBriefingContext>();
        record.Advice.Should().ContainSingle().Which.AdviceType.Should().Be("food_security");
        record.Flags.Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task RuleDecision_ReplacesBootstrapAdviceSnapshot()
    {
        Harness h = new((_, _, _, _) => Task.FromResult(new FoodLlmResponse(
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
        int calls = 0;
        Harness h = new((_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new FoodLlmResponse([], [])
                : new FoodLlmResponse(
                    "Food is below target and hunting may be viable.",
                    [FoodAdvice("llm_food")],
                    [new AgentFlag("food:llm", "Food", FlagSeverity.Medium, "food", "LLM food flag")]));
        });
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);
        h.SetFoodDays(12f, wildAnimals: 2, dateTimeRaw: "5th of Decembary, 5500, 14h", animalDef: "Wolf");
        await h.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.Id.Should().Be("llm_food");
        IReadOnlyDictionary<string, string> stateSummaries = h.Bus.ActiveSnapshot().StateSummaries!;
        stateSummaries.Should().ContainKey("Food")
            .WhoseValue.Should().Contain("Food stores show");
        stateSummaries["Food"].Should().Contain("12.0 days");
        stateSummaries["Food"].Should().NotBe("Food is below target and hunting may be viable.");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Summary.Should().Be("LLM food flag");
    }

    [Fact]
    public async Task EscalationFailure_PersistsFailedReplayRecord()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        Harness h = new((_, _, _, _) =>
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

    private static AdviceItem FoodAdvice(string id) => new(
        Id: id,
        Minister: "Food",
        AdviceType: "hunt_for_food",
        Priority: AdvicePriority.Medium,
        Title: "Hunt carefully",
        Body: "Use safe targets.",
        Rationale: "LLM selected hunting path.",
        ResourceRequests: [],
        SuggestedActions: [],
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
            Minister = new(Cache, new Rules(), new AgendaStore(), Bus, Flags, llm, retriever,
                NullLogger<MinisterOfFood>.Instance, replayRecorder);
        }

        public void SetFoodDays(float days, int wildAnimals = 0, string dateTimeRaw = "5th of Aprimay, 5500, 14h", string animalDef = "Hare")
        {
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
