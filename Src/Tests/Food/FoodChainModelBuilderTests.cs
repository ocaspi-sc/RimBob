using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Food;

namespace RimBob.Tests.Food;

public sealed class FoodChainModelBuilderTests
{
    [Fact]
    public void Build_UnhydratedState_ReturnsEmptyModel()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(12f) with
        {
            DataCoverage = new FoodDataCoverage(
                HasPlantPositions: false,
                HasAnimalPositions: false,
                HasZoneCells: false,
                HasBuildingPositions: false,
                HasWorkPriorities: false,
                HasTradeAvailability: false)
        };

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, []);

        model.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Build_HarvestAdvice_HighlightsGrowHarvestStep()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(18f) with
        {
            ReadyToHarvest = 9,
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "growing:1", 12, 1f, 9, "nearby to kitchen")]
        };
        AdviceItem advice = DecisionAdvice(briefing);

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, [advice]);

        Step(model, "grow.trigger").Status.Should().Be(AdviceChainStepStatus.Trigger);
        Step(model, "grow.zone").Status.Should().Be(AdviceChainStepStatus.Have);
        Step(model, "grow.harvest").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "grow.harvest").Detail.Should().Contain("9 tiles ready");
    }

    [Fact]
    public void Build_HarvestAdviceWithFreezerBlueprint_HighlightsStoreStep()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(18f) with
        {
            FoodUnits = 24,
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1),
            ReadyToHarvest = 9,
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "growing:1", 12, 1f, 9, "nearby to kitchen")]
        };
        AdviceItem advice = DecisionAdvice(briefing);

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, [advice]);

        Step(model, "grow.harvest").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "grow.store").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "hunt.store").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "forage.store").Status.Should().Be(AdviceChainStepStatus.Action);
    }

    [Fact]
    public void Build_HuntAdvice_HighlightsHuntAndBlocksMissingButcherTable()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(12f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 2,
            WildHuntTargets = [new WildHuntTarget("Hare", 2, "nearby to kitchen", "kitchen")],
            Kitchen = new FoodKitchenSummary(1, 0, true, false)
        };
        AdviceItem advice = DecisionAdvice(briefing);

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, [advice]);

        Step(model, "hunt.hunt").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "hunt.butcher").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "hunt.butcher").Detail.Should().Be("no butcher table visible");
        Step(model, "hunt.hunt").Detail.Should().Contain("2 hares");
    }

    [Fact]
    public void Build_CookBillAdvice_HighlightsCookStep()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Kitchen = new FoodKitchenSummary(1, 1, true, true)
        };
        AdviceItem advice = DecisionAdvice(briefing);

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, [advice]);

        Step(model, "grow.cook").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "hunt.cook").Status.Should().Be(AdviceChainStepStatus.Action);
        Step(model, "forage.cook").Status.Should().Be(AdviceChainStepStatus.Action);
    }

    [Fact]
    public void Build_LiveStableState_StillShowsAvailableFoodPaths()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(35f);

        AdviceChainModel model = FoodChainModelBuilder.Build(briefing, []);

        model.Paths.Should().HaveCount(3);
        Step(model, "grow.trigger").Status.Should().Be(AdviceChainStepStatus.Have);
        Step(model, "grow.store").Status.Should().Be(AdviceChainStepStatus.Available);
    }

    private static AdviceItem DecisionAdvice(FoodBriefing briefing)
    {
        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;
        return decision.Advice.Should().ContainSingle().Subject;
    }

    private static AdviceChainStep Step(AdviceChainModel model, string key) =>
        model.Paths.SelectMany(path => path.Steps).Single(step => step.Key == key);
}
