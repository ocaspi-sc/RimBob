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
    public async Task StableRuleDecision_DoesNotCallLlm()
    {
        bool called = false;
        Harness h = new((_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(new FoodLlmResponse([], []));
        });
        h.SetFoodDays(35f);

        await h.Minister.RunPlayCycle(CancellationToken.None);

        called.Should().BeFalse();
        h.PublishedAdvice.Should().BeEmpty();
    }

    [Fact]
    public async Task RuleDecision_PublishesAdviceAndFlag()
    {
        Harness h = new((_, _, _, _) => Task.FromResult(new FoodLlmResponse([], [])));
        h.SetFoodDays(4f);

        await h.Minister.RunPlayCycle(CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.AdviceType.Should().Be("food_security");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Domain.Should().Be("food");
    }

    [Fact]
    public async Task Escalation_UsesFoodLlmResponse()
    {
        Harness h = new((_, _, _, _) => Task.FromResult(new FoodLlmResponse(
            [FoodAdvice("llm_food")],
            [new AgentFlag("food:llm", "Food", FlagSeverity.Medium, "food", "LLM food flag")])));
        h.SetFoodDays(12f, wildAnimals: 2);

        await h.Minister.RunPlayCycle(CancellationToken.None);

        h.PublishedAdvice.Should().ContainSingle().Which.Id.Should().Be("llm_food");
        h.Flags.Active(FlagSeverity.Medium).Should().ContainSingle().Which.Summary.Should().Be("LLM food flag");
    }

    private static AdviceItem FoodAdvice(string id) => new(
        Id: id,
        Minister: "Food",
        AdviceType: "hunt_for_food",
        Severity: AdviceSeverity.Medium,
        Title: "Hunt carefully",
        Body: "Use safe targets.",
        Rationale: "LLM selected hunting path.",
        ResourceRequests: [],
        SuggestedActions: [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: DateTimeOffset.UnixEpoch.AddHours(4));

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public BriefingCache Cache { get; }
        public AdviceBus Bus { get; } = new();
        public FlagChannel Flags { get; } = new();
        public MinisterOfFood Minister { get; }
        public List<AdviceItem> PublishedAdvice { get; } = [];

        public Harness(LlmClient.FoodCallExecutor executor)
        {
            Cache = new(Colony, new TestLogger<BriefingCache>());
            Bus.AdvicePublished += PublishedAdvice.Add;
            LlmClient llm = new(NullLogger<LlmClient>.Instance, executor);
            FoodRagRetriever retriever = new(new KnowledgeBase(), null, false, 0, NullLogger<FoodRagRetriever>.Instance);
            Minister = new(Cache, new Rules(), new AgendaStore(), Bus, Flags, llm, retriever, NullLogger<MinisterOfFood>.Instance);
        }

        public void SetFoodDays(float days, int wildAnimals = 0)
        {
            Colony.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, "5th of Aprimay, 5500, 14h"));
            Colony.Colonists.Update(new ColonistRegistry([
                new ColonistRecord("p1", "P1", 30, "Female", 1f, 0.7f, 1f, false, false, null, [], []),
                new ColonistRecord("p2", "P2", 30, "Female", 1f, 0.7f, 1f, false, false, null, [], []),
                new ColonistRecord("p3", "P3", 30, "Female", 1f, 0.7f, 1f, false, false, null, [], [])
            ]));
            Colony.Resources.Update(new ResourceSummary(100, 0f, 80, days * FoodNutrition.NutritionPerColonistPerDay * 3, 20, 20, 0, 0, 0f));
            Colony.Buildings.Update(new BuildingRegistry([new BuildingRecord("cooler1", "Cooler", 1f, null, null)]));
            Colony.Animals.Update(new AnimalRegistry(Enumerable.Range(0, wildAnimals)
                .Select(i => new AnimalRecord($"a{i}", "Hare", false, 1f))
                .ToList()));
        }
    }
}
