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
        FoodBriefing briefing = Briefing(days: 4f);
        RulesResult result = new Rules().Evaluate(briefing, ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("food_security");
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.IssuedGameTick.Should().Be(briefing.GameTick);
        advice.ExpiresGameTick.Should().Be(briefing.GameTick + AdviceFreshness.TicksPerGameDay);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.SetPriority);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.Trade);
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.SelectedRule.Should().Be("emergency_food_flag");
        decision.Diagnostics.MatchedSignals.Should().Contain(signal =>
            signal.Rule == "emergency_food_flag" &&
            signal.Outcome == "selected");
        decision.Diagnostics.SuppressedCandidates.Should().Contain(signal =>
            signal.Rule == "expand_growing_capacity" &&
            signal.Outcome == "suppressed");
        decision.Diagnostics.AllRules.Should().HaveCount(13);
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "emergency_food_flag" &&
            row.Outcome == "selected" &&
            row.Conditions.Contains("EstimatedDaysOfFood < 7") &&
            row.OutputAction.Contains("immediate food-chain actions"));
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "nutrition_signal_gap" &&
            row.Outcome == "not_matched" &&
            row.Conditions.Contains("UnclassifiedFoodUnits > 0"));
        decision.Diagnostics.EmittedAdvice.Should().ContainSingle(row =>
            row.Source == "rules" &&
            row.Rule == "emergency_food_flag" &&
            row.AdviceId == advice.Id &&
            row.ActionCount == advice.Actions.Count);
        decision.Diagnostics.EmittedActions.Should().HaveCount(advice.Actions.Count);
        decision.Diagnostics.EmittedActions.Should().OnlyContain(row =>
            row.Source == "rules" &&
            row.Rule == "emergency_food_flag" &&
            row.AdviceId == advice.Id);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Severity.Should().Be(FlagSeverity.High);
        flag.LaborRequests.Should().NotBeNull();
        flag.LaborRequests!.Should().Contain(r => r.WorkType == WorkType.Cook);
        flag.Attention.Should().NotContain(r => r.Request.Contains("emergency food acquisition", StringComparison.OrdinalIgnoreCase));
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
        decision.Flags.Should().ContainSingle().Which.ItemRequests.Should().Contain(r =>
            r.Quantity == 7 &&
            r.Request.Contains("forbidden packaged survival meals"));
        AdviceAction unforbidStep = advice.Actions.Should().Contain(Action =>
            Action.Kind == AdviceActionKind.Unforbid &&
            Action.Instruction.Contains("Unforbid 7 packaged survival meals")).Subject;
        unforbidStep.Apply.Should().NotBeNull();
        unforbidStep.Apply!.Kind.Should().Be(AdviceApplyKind.UnforbidThings);
        UnforbidThingsApply unforbidApply = unforbidStep.Apply.Should().BeOfType<UnforbidThingsApply>().Subject;
        unforbidApply.ThingTargets.Should().ContainSingle()
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
        flag.Attention.Should().Contain(r => r.Request.Contains("growing tiles", StringComparison.OrdinalIgnoreCase));
        flag.BuildingRequests.Should().Contain(r => r.TargetClass == BuildingClass.ProductionBench);
        flag.LaborRequests.Should().Contain(r => r.WorkType == WorkType.Grow);
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
        flag.BuildingRequests.Should().Contain(r =>
            r.TargetClass == BuildingClass.Stockpile &&
            r.Quantity == 46);
        flag.Attention.Should().Contain(r => r.Request.Contains("growing tiles", StringComparison.OrdinalIgnoreCase));
        flag.BuildingRequests.Should().Contain(r => r.TargetClass == BuildingClass.ProductionBench);
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
        advice.Concern.Should().Be("harvest_now");
        AdviceAction Action = advice.Actions.Single();
        Action.Instruction.Should().Contain("nearby to kitchen");
        Action.Apply.Should().NotBeNull();
        Action.Apply!.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        decision.Flags.Should().BeEmpty();
    }

    [Fact]
    public void NoCoolerWithStableBuffer_EmitsFreezerMissingAdvice()
    {
        FoodBriefing briefing = Briefing(days: 25f) with
        {
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("freezer_missing");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("manage_freezer");
        advice.Priority.Should().Be(AdvicePriority.Medium);
        advice.Actions.Should().ContainSingle().Which.Kind.Should().Be(AdviceActionKind.PlaceBlueprint);
        advice.Actions.Single().Owner.Should().Be("Construction");
        decision.Flags.Should().ContainSingle().Which.BuildingRequests.Should()
            .Contain(r => r.TargetClass == BuildingClass.Freezer && r.RequestedFrom == "Construction");
    }

    [Fact]
    public void MatureCropsWithoutCooler_AsksForFreezerBeforeHarvestSpoils()
    {
        FoodBriefing briefing = Briefing(days: 18f) with
        {
            FoodUnits = 24,
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1),
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
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHarvest);
        AdviceAction freezerAction = advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Construction").Subject;
        freezerAction.Instruction.Should().Contain("starter freezer");
        freezerAction.Instruction.Should().Contain("next harvest");
        decision.Flags.Should().ContainSingle().Which.BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.RequestedFrom == "Construction" &&
            request.Request.Contains("starter freezer"));
    }

    [Fact]
    public void MealsUnderstocked_EmitsCookBillStep()
    {
        FoodBriefing briefing = Briefing(days: 12f) with { MealsCount = 1, RawFoodCount = 40, ReadyToHarvest = 0 };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("manage_cook_bills");
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.RequestResource);
        decision.Flags.Should().BeEmpty();
    }

    [Fact]
    public void MealsUnderstockedWithoutCooler_AsksForFreezerForCookingPath()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            FoodUnits = 40,
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("manage_cook_bills");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill);
        AdviceAction freezerAction = advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Construction").Subject;
        freezerAction.Instruction.Should().Contain("raw food and cooked meals");
        decision.Flags.Should().ContainSingle().Which.BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.Request.Contains("starter freezer"));
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
                CookingBuildingIds = ["stove-1"],
                CookingBuildingDetails =
                [
                    new FoodCookingBuildingSummary("stove-1", "FueledStove", "fueled stove", new(93, 0, 186))
                ]
            }
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction billAction = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill).Subject;
        billAction.Instruction.Should().Contain("fueled stove at (93, 0, 186)");
        billAction.Apply.Should().NotBeNull();
        billAction.Apply!.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
        billAction.Apply.TargetSummary.Should().Contain("fueled stove at (93, 0, 186)");
        UpsertProductionBillApply billApply = billAction.Apply.Should().BeOfType<UpsertProductionBillApply>().Subject;
        billApply.WorkbenchBuildingId.Should().Be("stove-1");
        billApply.RecipeSelectorKey.Should().Be("simple_meal");
        billApply.RepeatMode.Should().Be("TargetCount");
        billApply.TargetCount.Should().Be(12);
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
    public void MealsUnderstocked_WithSatisfiedSimpleMealBill_DoesNotRepeatCookBillAdvice()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Kitchen = KitchenWithSimpleMealBill(targetCount: 12)
        };

        RulesResult result = new Rules().Evaluate(briefing, ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        decision.Trace.Should().NotBe("meals_understocked");
        decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().NotContain(action => action.Kind == AdviceActionKind.ProductionBill);
    }

    [Fact]
    public void UrgentShortage_WithSatisfiedSimpleMealBill_KeepsCookLaborAsFlagRequest()
    {
        FoodBriefing briefing = Briefing(days: 4f) with
        {
            Kitchen = KitchenWithSimpleMealBill(targetCount: 12)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
        decision.Flags.Should().ContainSingle().Which.LaborRequests.Should().Contain(request =>
            request.WorkType == WorkType.Cook &&
            request.RequestedFrom == "Labor");
    }

    [Fact]
    public void UrgentShortage_WithOneCookingWorkbench_AttachesCookBillApply()
    {
        FoodBriefing briefing = Briefing(days: 4f) with
        {
            Kitchen = new FoodKitchenSummary(1, 1, true, true)
            {
                CookingBuildingIds = ["stove-1"],
                CookingBuildingDetails =
                [
                    new FoodCookingBuildingSummary("stove-1", "FueledStove", "fueled stove", new(93, 0, 186))
                ]
            }
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction billAction = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill).Subject;
        billAction.Instruction.Should().Contain("fueled stove at (93, 0, 186)");
        billAction.Apply.Should().NotBeNull();
        billAction.Apply!.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
    }

    [Fact]
    public void MealsUnderstocked_WithNoCookCoverage_RequestsCookWorkTypeInFlag()
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
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
        decision.Flags.Should().ContainSingle().Which.LaborRequests.Should().Contain(request =>
            request.WorkType == WorkType.Cook &&
            request.Skill == "Cooking");
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
        advice.Concern.Should().Be("wild_harvest");
        advice.Title.Should().Be("Forage can extend the buffer");
        advice.Rationale.Should().Contain("Foraging edible plants");
        AdviceAction action = advice.Actions.Single();
        action.Instruction.Should().Contain("nearest 6 Plant_Berry");
        action.Instruction.Should().NotContain("wild");
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
        MarkHarvestAreaApply harvestApply = Action.Apply.Should().BeOfType<MarkHarvestAreaApply>().Subject;
        harvestApply.TargetIds.Should().Equal("berry-1", "berry-2");
    }

    [Fact]
    public void WildHarvest_WithBoundedApplySubset_LabelsDisplayedSubset()
    {
        FoodBriefing briefing = Briefing(days: 14f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 111,
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 111, 1f, "nearby to kitchen", "kitchen")],
            HarvestTargets =
            [
                new FoodHarvestTarget(
                    "wild",
                    "Plant_Berry",
                    40,
                    new(10, 10, 19, 13),
                    Enumerable.Range(0, 40).Select(i => $"berry-{i}").ToList(),
                    null,
                    "nearby to kitchen",
                    "kitchen")
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction action = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().ContainSingle().Subject;
        action.Instruction.Should().Contain("Mark 40 of 111 Plant_Berry");
        action.Apply.Should().NotBeNull();
        action.Apply.Should().BeOfType<MarkHarvestAreaApply>().Subject.TargetCount.Should().Be(40);
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
        advice.Concern.Should().Be("expand_growing_capacity");
        AdviceAction Action = advice.Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.DesignateZone);
        Action.Quantity.Should().Be(36);
        Action.Instruction.Should().Contain("rice");
        advice.Body.Should().Contain("winter margin");
    }

    [Fact]
    public void LowBufferWithActiveCropCoverage_DoesNotRequestDuplicateGrowingZone()
    {
        FoodBriefing briefing = Briefing(days: 16f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 0,
            CropBreakdown = [new FoodCropSummary("Plant_Rice", 36, 0.05f)],
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "2", 36, 0.05f, 0, "nearby to storage")]
        };

        Escalate escalation = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Escalate>()
            .Subject;

        escalation.Diagnostics.Should().NotBeNull();
        escalation.Diagnostics!.SelectedRule.Should().Be("unresolved_food_gap");
        escalation.Diagnostics.MatchedSignals.Should().NotContain(signal =>
            signal.Rule == "expand_growing_capacity");
        escalation.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "expand_growing_capacity" &&
            row.Outcome == "not_matched" &&
            row.Conditions.Contains("active matching crop coverage is below candidate tile target"));
    }

    [Fact]
    public void UrgentShortageWithActiveCropCoverage_DoesNotRepeatGrowingZone()
    {
        FoodBriefing briefing = Briefing(days: 6f) with
        {
            FoodUnits = 28,
            MealsCount = 20,
            RawFoodCount = 8,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 111,
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 111, 1f, "nearby to kitchen", "kitchen")],
            HarvestTargets =
            [
                new FoodHarvestTarget(
                    "wild",
                    "Plant_Berry",
                    40,
                    new(10, 10, 19, 13),
                    Enumerable.Range(0, 40).Select(i => $"berry-{i}").ToList(),
                    null,
                    "nearby to kitchen",
                    "kitchen")
            ],
            WildAnimalCount = 53,
            WildHuntTargets = [new WildHuntTarget("Hare", 1, "far from kitchen", "kitchen")],
            HuntTargets =
            [
                new FoodHuntTarget("Hare", 1, new(40, 50, 40, 50), ["hare-1"], "far from kitchen", "kitchen")
            ],
            CropBreakdown = [new FoodCropSummary("Plant_Rice", 36, 0.05f)],
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "2", 36, 0.05f, 0, "nearby to storage")],
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 0),
            Kitchen = KitchenWithSimpleMealBill(targetCount: 12)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Actions.Should().HaveCount(3);
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHarvest);
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHunt);
        advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Construction" &&
            action.Instruction.Contains("foraged food"));
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.DesignateZone);
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Attention.Should().Contain(request => request.Request.Contains("cook simple meals", StringComparison.OrdinalIgnoreCase));
        flag.LaborRequests.Should().NotContain(request => request.WorkType == WorkType.Grow);
        flag.LaborRequests.Should().Contain(request =>
            request.WorkType == WorkType.Cook &&
            request.RequestedFrom == "Labor");
        flag.BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.RequestedFrom == "Construction" &&
            request.Request.Contains("surplus freezer"));
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
        decision.Advice.Should().ContainSingle().Subject.Body.Should().Contain("1 day of winter margin");
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

        Escalate escalation = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Escalate>()
            .Subject;

        escalation.Reason.Should().Contain("Winter is close");
        escalation.Diagnostics.Should().NotBeNull();
        escalation.Diagnostics!.SelectedRule.Should().Be("winter_food_tradeoff");
        escalation.Diagnostics.MatchedSignals.Should().Contain(signal =>
            signal.Rule == "winter_food_tradeoff" &&
            signal.Outcome == "escalated");
        escalation.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "winter_food_tradeoff" &&
            row.Outcome == "escalated" &&
            row.OutputAction.Contains("Escalate to LLM"));
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
            WildAnimalCount = 2,
            WildHuntTargets = [new WildHuntTarget("Hare", 2, "nearby to kitchen", "kitchen")],
            HuntTargets =
            [
                new FoodHuntTarget("Hare", 2, new(40, 50, 41, 50), ["hare-1", "hare-2"], "nearby to kitchen", "kitchen")
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("hunt_low_risk_animals");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "hunt_targets_blocked_by_risk" &&
            row.Outcome == "not_matched");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("hunt_for_food");
        AdviceAction Action = advice.Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.MarkHunt);
        Action.Instruction.Should().Be("Mark up to 2 hares for hunting (nearby to kitchen).");
        Action.Instruction.Should().NotContain("skip predators");
        Action.Instruction.Should().NotContain("tame");
        Action.Instruction.Should().NotContain("high-revenge");
        Action.WorkType.Should().Be(WorkType.Hunt);
        Action.Skill.Should().Be("Shooting");
        Action.Apply.Should().NotBeNull();
        Action.Apply!.Kind.Should().Be(AdviceApplyKind.MarkHuntArea);
        MarkHuntAreaApply huntApply = Action.Apply.Should().BeOfType<MarkHuntAreaApply>().Subject;
        huntApply.TargetIds.Should().Equal("hare-1", "hare-2");
        decision.Diagnostics.EmittedActions.Should().ContainSingle(row =>
            row.Source == "rules" &&
            row.Rule == "hunt_low_risk_animals" &&
            row.AdviceId == advice.Id &&
            row.ActionIndex == 0 &&
            row.Kind == AdviceActionKind.MarkHunt &&
            row.ApplyKind == AdviceApplyKind.MarkHuntArea &&
            row.ApplyLabel == "Mark hunt");
    }

    [Fact]
    public void LowBufferHuntWithoutCooler_AsksForFreezerBeforeMeatSpoils()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            FoodUnits = 20,
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 2,
            WildHuntTargets = [new WildHuntTarget("Hare", 2, "nearby to kitchen", "kitchen")],
            HuntTargets =
            [
                new FoodHuntTarget("Hare", 2, new(40, 50, 41, 50), ["hare-1", "hare-2"], "nearby to kitchen", "kitchen")
            ],
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1)
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("hunt_for_food");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHunt);
        AdviceAction freezerAction = advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Construction").Subject;
        freezerAction.Instruction.Should().Contain("hunted meat");
        decision.Flags.Should().ContainSingle().Which.BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.Request.Contains("starter freezer"));
    }

    [Fact]
    public void LowBufferWithHuntSummaryButNoExactTarget_StaysTextOnly()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 3,
            WildHuntTargets = [new WildHuntTarget("Hare", 3, "location unknown", null)]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceAction action = decision.Advice.Should().ContainSingle().Subject
            .Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.MarkHunt);
        action.Apply.Should().BeNull();
    }

    [Fact]
    public void LowBufferWithScoredHuntTarget_ExplainsNutritionValue()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 0,
            WildAnimalCount = 4,
            WildHuntTargets =
            [
                new WildHuntTarget("Ibex", 4, "nearby to kitchen", "kitchen")
                {
                    EstimatedNutrition = 14f,
                    ScoreReason = "about 14 nutrition; about 3.5 nutrition each"
                }
            ]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Body.Should().Contain("about 14 nutrition");
        advice.Actions.Should().ContainSingle().Which.Instruction.Should().Contain("ibex");
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
        advice.Concern.Should().Be("manage_food_stockpile");
        advice.Body.Should().Contain("25 unknown food units");
        advice.Body.Should().NotContain("audit");
    }

    [Fact]
    public void HuntTargetsBlockedByRisk_Escalates()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            Season = new SeasonContext("Decembary", 5, 0),
            WildAnimalCount = 3,
            ReadyToHarvest = 0
        };

        Escalate escalation = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Escalate>()
            .Subject;

        escalation.Reason.Should().Contain("visible animals");
        escalation.Diagnostics.Should().NotBeNull();
        escalation.Diagnostics!.SelectedRule.Should().Be("hunt_targets_blocked_by_risk");
        escalation.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "hunt_targets_blocked_by_risk" &&
            row.Outcome == "escalated" &&
            row.OutputAction.Contains("hunt safety/risk context"));
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

    private static FoodKitchenSummary KitchenWithSimpleMealBill(int targetCount) =>
        new(1, 1, true, true)
        {
            CookingBuildingIds = ["stove-1"],
            CookingBuildingDetails =
            [
                new FoodCookingBuildingSummary("stove-1", "FueledStove", "fueled stove", new(93, 0, 186))
            ],
            CookingBills =
            [
                new FoodCookingBillSummary(
                    WorkbenchBuildingId: "stove-1",
                    LoadId: 0,
                    RecipeDefName: "CookMealSimple",
                    RecipeLabel: "cook simple meal",
                    Suspended: false,
                    Paused: false,
                    RepeatMode: "TargetCount",
                    RepeatCount: 1,
                    TargetCount: targetCount)
            ]
        };
}
