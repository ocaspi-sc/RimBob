using FluentAssertions;
using RimBob.Core.Briefings;

namespace RimBob.Tests.Food;

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
                new FoodCropZoneSummary("Plant_Rice", "2", 12, 0.56f, 3, "nearby to kitchen"),
                new FoodCropZoneSummary("Plant_Corn", "growing:2", 8, 0.22f, 0, "far from kitchen")
            ],
            GrowingTerrain = new FoodGrowingTerrainSummary(
                HasTerrain: true,
                GrowableCells: 36,
                BestFertility: 1.4f,
                AverageFertility: 1.1f,
                FertilityBands: [new FoodTerrainFertilityBand("SoilRich", "rich soil", 1.4f, 12)]),
            Kitchen = new FoodKitchenSummary(1, 1, true, true),
            Infrastructure = new FoodInfrastructureSnapshot(1, true, 500f, 1),
            Storage = new FoodStorageSummary(1, 20, null, null)
            {
                PositionedFoodUnits = 21,
                CoolerAdjacentFoodUnits = 7
            },
            WildAnimalCount = 2,
            WildHuntTargets =
            [
                new WildHuntTarget("Hare", 2, "nearby to kitchen", "kitchen")
                {
                    EstimatedNutrition = 1.2f,
                    ScoreReason = "low-risk metadata"
                }
            ],
            HuntTargets =
            [
                new FoodHuntTarget("Hare", 2, new(40, 50, 41, 50), ["hare-1", "hare-2"], "nearby to kitchen", "kitchen")
            ],
            HuntRiskSummaries =
            [
                new FoodHuntRiskSummary("Hare", 2, "low", 1.2f, "low-risk metadata"),
                new FoodHuntRiskSummary("Ibex", 4, "caution", 3.6f, "herd animal revenge chance"),
                new FoodHuntRiskSummary("Bear_Grizzly", 1, "dangerous", 5.0f, "dangerous body size")
            ]
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().StartWith("- Stores:");
        summary.Should().Contain("7 meals");
        summary.Should().Contain("14 raw food");
        summary.Should().Contain("about 12.0 days for 3 colonists");
        summary.Should().Contain("\n- Crops:");
        summary.Should().Contain("12 rice plants in zone 2");
        summary.Should().Contain("3 tiles ready");
        summary.Should().Contain("8 corn plants in growing:2");
        summary.Should().Contain("terrain 36 growable cells");
        summary.Should().Contain("rich soil fertility 1.4");
        summary.Should().Contain("\n- Acquisition:");
        summary.Should().Contain("0 forage candidates");
        summary.Should().Contain("2 hares hunt targets");
        summary.Should().Contain("\n- Hunt risk:");
        summary.Should().Contain("1 low-risk type surfaced");
        summary.Should().Contain("best 2 hares, 1.2 nutrition, low-risk metadata");
        summary.Should().Contain("1 bounded apply target ready");
        summary.Should().Contain("1 caution type and 1 dangerous type held for escalation");
        summary.Should().Contain("\n- Kitchen/storage:");
        summary.Should().Contain("1 cooking station");
        summary.Should().Contain("1 cooler");
        summary.Should().Contain("1 stockpile zone / 20 stockpile cells");
        summary.Should().Contain("7 stored food units near coolers, 14 elsewhere");
    }

    [Fact]
    public void Build_CallsOutPositionedStoredFoodWithoutCoolerCoverage()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(12f) with
        {
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1),
            Storage = new FoodStorageSummary(1, 20, null, null)
            {
                PositionedFoodUnits = 21
            }
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().Contain("21 positioned stored food units with no cooler coverage");
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

        briefing.ExcludedFoodUnits.Should().Be(0);
        briefing.UnknownFoodUnits.Should().Be(25);
        summary.Should().Contain("25 unknown food units");
        summary.Should().Contain("days-of-food cannot be estimated");
        summary.Should().NotContain("edible");
    }

    [Fact]
    public void Build_UnclassifiedFoodIncludesForbiddenItemDetails()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(6.1f) with
        {
            FoodUnits = 52,
            MealsCount = 32,
            RawFoodCount = 12,
            UnclassifiedFoodItems =
            [
                new FoodUnclassifiedItem("MealSurvivalPack", "packaged survival meal", 7, "meal", true, "map_things", "(62,0,219)"),
                new FoodUnclassifiedItem("Corpse_Squirrel", "squirrel (dead)", 1, "raw_food", true, "map_things", "(83,0,38)")
            ]
        };

        string summary = FoodStateSummary.Build(briefing);

        briefing.ExcludedFoodUnits.Should().Be(8);
        briefing.UnknownFoodUnits.Should().Be(0);
        summary.Should().Contain("8 food units excluded from reachable buffer");
        summary.Should().NotContain("unknown food units");
        summary.Should().Contain("7 forbidden packaged survival meals at (62,0,219)");
        summary.Should().Contain("1 forbidden squirrel (dead) at (83,0,38)");
        summary.Should().NotContain("food_unit_classification");
    }

    [Fact]
    public void Build_HuntRiskLineShowsThreatGateWhenTargetsAreBlocked()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(6.1f) with
        {
            ActiveThreat = true,
            WildAnimalCount = 3,
            WildHuntTargets =
            [
                new WildHuntTarget("Rat", 3, "nearby to kitchen", "kitchen")
                {
                    EstimatedNutrition = 0.7f,
                    ScoreReason = "safe by fallback def-name check"
                }
            ],
            HuntRiskSummaries =
            [
                new FoodHuntRiskSummary("Rat", 3, "low", 0.7f, "safe by fallback def-name check")
            ]
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().Contain("Hunt risk: 1 low-risk type surfaced; best 3 rats, 0.7 nutrition, safe by fallback def-name check; active threat blocks mark_hunt.");
    }

    [Fact]
    public void Build_HuntRiskLineExplainsWhenVisibleAnimalsFailSafetyPrefilter()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(6.1f) with
        {
            WildAnimalCount = 3,
            WildHuntTargets = [],
            HuntRiskSummaries = []
        };

        string summary = FoodStateSummary.Build(briefing);

        summary.Should().Contain("Hunt risk: no healthy wild animal type passed the safety prefilter.");
    }

    [Fact]
    public void Build_DefaultStateUsesBullets()
    {
        string summary = FoodStateSummary.Build(FoodRulesTests.Briefing(12f) with
        {
            DataCoverage = new FoodDataCoverage(
                HasPlantPositions: false,
                HasAnimalPositions: false,
                HasZoneCells: false,
                HasBuildingPositions: false,
                HasWorkPriorities: false,
                HasTradeAvailability: false)
        });

        summary.Should().StartWith("- Live state:");
        summary.Should().Contain("\n- Refresh:");
    }
}
