using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Food;

namespace RimBob.Tests.Food;

public sealed class FoodRulesTests
{
    [Fact]
    public void StableFood_ReturnsNoAdvice()
    {
        RulesResult result = new Rules().Evaluate(Briefing(days: 35f), ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Trace.Should().Be("maintain_security_threshold");
    }

    [Fact]
    public void UrgentShortage_EmitsHighFoodSecurityAdviceAndFlag()
    {
        RulesResult result = new Rules().Evaluate(Briefing(days: 4f), ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("food_security");
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.SetPriority && s.WorkType == WorkType.Cook);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.Trade);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Severity.Should().Be(FlagSeverity.High);
        flag.Requests.Should().NotBeNull();
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Cook);
        flag.Requests!.Should().NotContain(r => r.Kind == ResourceRequestKind.TradeCapacity);
    }

    [Fact]
    public void UrgentShortageWithForbiddenMeals_SuggestsUnforbid()
    {
        FoodBriefing briefing = Briefing(days: 6.1f) with
        {
            FoodUnits = 52,
            MealsCount = 32,
            RawFoodCount = 12,
            UnclassifiedFoodItems =
            [
                new FoodUnclassifiedItem("MealSurvivalPack", "packaged survival meal", 7, "meal", true, "map_things", "(62,0,219)")
            ],
            UnforbidTargets =
            [
                new FoodUnforbidTarget("meal-forbidden", "MealSurvivalPack", "packaged survival meal", 7, "meal", "map_things", new(62, 0, 219))
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Body.Should().Contain("7 forbidden packaged survival meals");
        decision.Flags.Should().ContainSingle().Which.Requests.Should().Contain(r =>
            r.Kind == ResourceRequestKind.Item &&
            r.Quantity == 7 &&
            r.What.Contains("forbidden packaged survival meals"));
        AdviceAction unforbidStep = advice.Actions.Should().Contain(Action =>
            Action.Kind == AdviceActionKind.Unforbid &&
            Action.Instruction.Contains("Unforbid 7 packaged survival meals")).Subject;
        unforbidStep.Apply.Should().NotBeNull();
        unforbidStep.Apply!.Kind.Should().Be(AdviceApplyKind.UnforbidThings);
        unforbidStep.Apply.ThingTargets.Should().ContainSingle()
            .Which.Def.Should().Be("MealSurvivalPack");
    }

    [Fact]
    public void NearStarvationWithoutLocalFood_SetsUpFoodChain()
    {
        FoodBriefing briefing = Briefing(days: 0.4f) with
        {
            FoodUnits = 0,
            MealsCount = 0,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            Kitchen = new FoodKitchenSummary(0, 0, false, false),
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 0),
            Storage = new FoodStorageSummary(0, 0, null, null),
            StockpileCells = 0
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Priority.Should().Be(AdvicePriority.Critical);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.DesignateZone);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.PlaceBlueprint);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.Note);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Severity.Should().Be(FlagSeverity.Critical);
        flag.Requests.Should().NotBeNull();
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Tile);
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Building);
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Grow);
        flag.Requests!.Should().NotContain(r => r.Kind == ResourceRequestKind.Attention);
    }

    [Fact]
    public void NearStarvationWithUnclassifiedFood_RequestsStockpileVisibilityAndSetup()
    {
        FoodBriefing briefing = Briefing(days: 0.4f) with
        {
            FoodUnits = 46,
            MealsCount = 0,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            Kitchen = new FoodKitchenSummary(0, 1, false, true),
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 0),
            Storage = new FoodStorageSummary(0, 0, null, null),
            StockpileCells = 0
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Actions.Should().Contain(s =>
            s.Kind == AdviceActionKind.SetStockpileZone &&
            s.Quantity == 46);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.DesignateZone);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.PlaceBlueprint);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Requests.Should().NotBeNull();
        flag.Requests!.Should().Contain(r =>
            r.Kind == ResourceRequestKind.StockpileSpace &&
            r.Quantity == 46);
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Tile);
        flag.Requests!.Should().Contain(r => r.Kind == ResourceRequestKind.Building);
        flag.Requests!.Should().NotContain(r => r.Kind == ResourceRequestKind.Attention);
    }

    [Fact]
    public void MatureCrops_EmitsHarvestAdvice()
    {
        FoodBriefing briefing = Briefing(days: 18f) with
        {
            ReadyToHarvest = 9,
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "growing:1", 12, 1f, 9, "nearby to kitchen")],
            HarvestTargets =
            [
                new FoodHarvestTarget("crop", "Plant_Rice", 9, new(10, 20, 12, 22), ["rice-1"], "growing:1", "nearby to kitchen", "kitchen")
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("harvest_mature_crops");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("harvest_now");
        AdviceAction Action = advice.Actions.Single();
        Action.Instruction.Should().Contain("nearby to kitchen");
        Action.Apply.Should().NotBeNull();
        Action.Apply!.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        decision.Flags.Should().BeEmpty();
    }

    [Fact]
    public void MealsUnderstocked_EmitsCookBillStep()
    {
        FoodBriefing briefing = Briefing(days: 12f) with { MealsCount = 1, RawFoodCount = 40, ReadyToHarvest = 0 };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("manage_cook_bills");
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.RequestResource);
        decision.Flags.Should().BeEmpty();
    }

    [Fact]
    public void MealsUnderstocked_WithOneCookingWorkbench_AttachesCookBillApply()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Kitchen = new FoodKitchenSummary(1, 1, true, true)
            {
                CookingBuildingIds = ["stove-1"]
            }
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction billAction = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill).Subject;
        billAction.Apply.Should().NotBeNull();
        billAction.Apply!.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
        billAction.Apply.WorkbenchBuildingId.Should().Be("stove-1");
        billAction.Apply.RecipeSelectorKey.Should().Be("simple_meal");
        billAction.Apply.RepeatMode.Should().Be("TargetCount");
        billAction.Apply.TargetCount.Should().Be(12);
    }

    [Fact]
    public void MealsUnderstocked_WithAmbiguousCookingWorkbenches_StaysTextOnly()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Kitchen = new FoodKitchenSummary(2, 1, true, true)
            {
                CookingBuildingIds = ["stove-1", "campfire-1"]
            }
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction billAction = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill).Subject;
        billAction.Apply.Should().BeNull();
    }

    [Fact]
    public void UrgentShortage_WithOneCookingWorkbench_AttachesCookBillApply()
    {
        FoodBriefing briefing = Briefing(days: 4f) with
        {
            Kitchen = new FoodKitchenSummary(1, 1, true, true)
            {
                CookingBuildingIds = ["stove-1"]
            }
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction billAction = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill).Subject;
        billAction.Apply.Should().NotBeNull();
        billAction.Apply!.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
    }

    [Fact]
    public void MealsUnderstocked_WithNoCookCoverage_RequestsCookWorkType()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Skills = new FoodSkillSnapshot(10, 1, 4, 0)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Actions.Should().Contain(s =>
            s.Kind == AdviceActionKind.SetPriority &&
            s.WorkType == WorkType.Cook &&
            s.Skill == "Cooking");
    }

    [Fact]
    public void WildHarvest_UsesNearestClusterAndAvoidsRoutineLaborRequest()
    {
        FoodBriefing briefing = Briefing(days: 14f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 6,
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 6, 1f, "nearby to kitchen", "kitchen")],
            HarvestTargets =
            [
                new FoodHarvestTarget("wild", "Plant_Berry", 6, new(30, 40, 32, 41), ["berry-1"], null, "nearby to kitchen", "kitchen")
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("wild_harvest");
        advice.Title.Should().Be("Forage can extend the buffer");
        advice.Rationale.Should().Contain("Foraging edible plants");
        AdviceAction action = advice.Actions.Single();
        action.Instruction.Should().Contain("nearest 6 Plant_Berry");
        action.Instruction.Should().NotContain("wild");
        action.Reason.Should().Be("edible forage requires plant work");
        action.Apply.Should().NotBeNull();
        action.Apply!.Label.Should().Be("Mark forage");
        decision.Flags.Should().BeEmpty();
    }

    [Fact]
    public void WildHarvest_ApplyTargetMatchesDisplayedCluster()
    {
        FoodBriefing briefing = Briefing(days: 14f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 10,
            WildHarvestClusters =
            [
                new WildHarvestCluster("Plant_Berry", 2, 1f, "nearby to kitchen", "kitchen"),
                new WildHarvestCluster("Plant_Agave", 8, 1f, "far from kitchen", "kitchen")
            ],
            HarvestTargets =
            [
                new FoodHarvestTarget("wild", "Plant_Agave", 8, new(80, 80, 82, 82), ["agave-1"], null, "far from kitchen", "kitchen"),
                new FoodHarvestTarget("wild", "Plant_Berry", 2, new(30, 40, 31, 40), ["berry-1", "berry-2"], null, "nearby to kitchen", "kitchen")
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction Action = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().ContainSingle().Subject;
        Action.Instruction.Should().Contain("nearest 2 Plant_Berry");
        Action.Apply.Should().NotBeNull();
        Action.Apply!.TargetSummary.Should().Contain("Plant_Berry");
        Action.Apply.TargetIds.Should().Equal("berry-1", "berry-2");
    }

    [Fact]
    public void LowBufferDuringGrowingSeason_RequestsFoodGrowingTiles()
    {
        FoodBriefing briefing = Briefing(days: 16f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 0
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("expand_growing_capacity");
        AdviceAction Action = advice.Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.DesignateZone);
        Action.Quantity.Should().Be(36);
        Action.Instruction.Should().Contain("rice");
        Action.Reason.Should().Contain("winter margin");
    }

    [Fact]
    public void LowBufferNearWinter_UsesFastCropWhenItStillFits()
    {
        FoodBriefing briefing = Briefing(days: 16f) with
        {
            Season = new SeasonContext("Decembary", 2, 5),
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 0
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction Action = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.DesignateZone);
        Action.Instruction.Should().Contain("rice");
        Action.Reason.Should().Contain("1 day of winter margin");
    }

    [Fact]
    public void LowBufferTooCloseToWinter_EscalatesInsteadOfSowing()
    {
        FoodBriefing briefing = Briefing(days: 16f) with
        {
            Season = new SeasonContext("Decembary", 2, 2),
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 0
        };

        new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Escalate>()
            .Which.Reason.Should().Contain("Winter is close");
    }

    [Fact]
    public void LowBufferWithLowRiskHuntTarget_MarksAnimalsForHunting()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 3,
            WildHuntTargets = [new WildHuntTarget("Hare", 3, "nearby to kitchen", "kitchen")]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("hunt_low_risk_animals");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("hunt_for_food");
        AdviceAction Action = advice.Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.MarkHunt);
        Action.WorkType.Should().Be(WorkType.Hunt);
        Action.Skill.Should().Be("Shooting");
    }

    [Fact]
    public void NutritionGapWithUnknownFoodUnits_RequestsStockpileVisibility()
    {
        FoodBriefing briefing = Briefing(days: null) with
        {
            FoodUnits = 25,
            MealsCount = 0,
            RawFoodCount = 0,
            NutritionSource = "unknown"
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("nutrition_signal_gap");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("manage_food_stockpile");
        advice.Body.Should().Contain("25 unknown food units");
        advice.Body.Should().NotContain("audit");
    }

    [Fact]
    public void HuntingAmbiguity_Escalates()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            Season = new SeasonContext("Decembary", 5, 0),
            WildAnimalCount = 3,
            ReadyToHarvest = 0
        };

        new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Escalate>()
            .Which.Reason.Should().Contain("hunting");
    }

    internal static FoodBriefing Briefing(float? days) => new(
        BriefingVersion: 1,
        Date: new DateStamp("5th of Aprimay, 5500, 14h", 1, "Aprimay", 5, 14),
        GameTick: 300_000,
        Season: new SeasonContext("Aprimay", 11, 50),
        ColonistCount: 3,
        ReportedNutrition: days is null ? null : days * 4.8f,
        FallbackNutrition: null,
        NutritionSource: days is null ? "unknown" : "reported",
        EstimatedDaysOfFood: days,
        FoodUnits: 60,
        MealsCount: 20,
        RawFoodCount: 20,
        ReadyToHarvest: 0,
        CropBreakdown: [],
        CropZoneSummaries: [],
        WildHarvestCandidates: 0,
        WildHarvestClusters: [],
        WildAnimalCount: 0,
        WildHuntTargets: [],
        StockpileCells: 20,
        Skills: new FoodSkillSnapshot(10, 1, 8, 1),
        Infrastructure: new FoodInfrastructureSnapshot(1, true, 500f, 1),
        Storage: new FoodStorageSummary(1, 20, null, null),
        Kitchen: new FoodKitchenSummary(1, 1, true, true),
        DataCoverage: new FoodDataCoverage(false, false, false, false, false, false)
        {
            HasLiveState = true
        },
        ActiveThreat: false,
        RecentFoodIncidents: []
    );
}
