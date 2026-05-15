using FluentAssertions;
using RimAI.Core.Briefings;

namespace RimAI.Tests.Food;

public sealed class FoodStateSummaryTests
{
    [Fact]
    public void Build_SummarizesStoredFoodGrowingAreasAndKitchenState()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(12f) with
        {
            MealsCount = 7,
            RawFoodCount = 14,
            CropZoneSummaries =
            [
                new FoodCropZoneSummary("Plant_Rice", "growing:1", 12, 0.56f, 3, "nearby to kitchen"),
                new FoodCropZoneSummary("Plant_Corn", "growing:2", 8, 0.22f, 0, "far from kitchen")
            ],
            Kitchen = new FoodKitchenSummary(1, 1, true, true),
            Infrastructure = new FoodInfrastructureSnapshot(1, true, 500f, 1),
            Storage = new FoodStorageSummary(1, 20, null, null)
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().Contain("7 meals");
        summary.Should().Contain("14 raw food");
        summary.Should().Contain("about 12.0 days for 3 colonists");
        summary.Should().Contain("Growing areas:");
        summary.Should().Contain("12 rice plants in growing:1");
        summary.Should().Contain("3 tiles ready");
        summary.Should().Contain("8 corn plants in growing:2");
        summary.Should().Contain("Kitchen/storage:");
        summary.Should().Contain("1 cooking station");
        summary.Should().Contain("1 cooler");
        summary.Should().Contain("1 stockpile zone / 20 stockpile cells");
    }

    [Fact]
    public void Build_UnclassifiedFoodExplainsVisibilityGapWithoutCallingItEdible()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(null) with
        {
            FoodUnits = 25,
            MealsCount = 0,
            RawFoodCount = 0,
            CropZoneSummaries = []
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().Contain("25 unclassified food units");
        summary.Should().Contain("days-of-food cannot be estimated");
        summary.Should().NotContain("edible");
    }
}
