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
        RuleRun result = new Rules().Evaluate(Briefing(days: 35f), ColonyContext.Default);

        ProjectedRuleRun decision = Project(result);
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Trace.Should().Be("maintain_security_threshold");
    }

    [Fact]
    public void UrgentShortage_EmitsHighFoodSecurityAdviceAndFlag()
    {
        FoodBriefing briefing = Briefing(days: 4f);
        RuleRun result = new Rules().Evaluate(briefing, ColonyContext.Default);

        ProjectedRuleRun decision = Project(result);
        AdviceItem advice = AdviceByRule(decision, "emergency_food_flag");
        advice.Priority.Should().Be(Priority.High);
        advice.IssuedGameTick.Should().Be(briefing.GameTick);
        advice.ExpiresGameTick.Should().Be(briefing.GameTick + AdviceFreshness.TicksPerGameDay);
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.RequestResource);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.SetPriority);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.Trade);
        DecisionShouldIncludeRules(decision, "emergency_food_flag", "expand_growing_capacity");
        decision.Diagnostics.Should().NotBeNull();
        decision.Trace.Should().Be("rules:emergency_food_flag+expand_growing_capacity");
        decision.Diagnostics.MatchedSignals.Should().Contain(signal =>
            signal.Rule == "emergency_food_flag" &&
            signal.Outcome == RuleOutcome.Selected);
        decision.Diagnostics.MatchedSignals.Should().Contain(signal =>
            signal.Rule == "expand_growing_capacity" &&
            signal.Outcome == RuleOutcome.Selected);
        decision.Diagnostics.AllRules.Should().HaveCount(13);
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "emergency_food_flag" &&
            row.Outcome == RuleOutcome.Selected &&
            row.Conditions.Contains("below the 7d emergency threshold") &&
            row.OutputAction.Contains("actions: request_resource") &&
            row.OutputAction.Contains("requests: 1"));
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "nutrition_signal_gap" &&
            row.Outcome == RuleOutcome.NotMatched &&
            row.Conditions.Contains("days-of-food is available"));
        decision.Effects.OfType<Advise>().Should().Contain(effect =>
            effect.Rule == "emergency_food_flag" &&
            effect.Actions.Count == advice.Actions.Count);
        AgentFlag flag = FlagById(decision, "food:emergency_food_flag");
        flag.Priority.Should().Be(Priority.High);
        flag.Attention.Should().NotBeNull();
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
            UnforbidTargets =
            [
                new FoodUnforbidTarget("meal-forbidden", "MealSurvivalPack", "packaged survival meal", 7, "meal", "map_things", new(62, 0, 219))
            ]
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "emergency_food_flag");
        advice.Body.Should().Contain("7 forbidden packaged survival meals");
        FlagById(decision, "food:emergency_food_flag").ItemRequests.Should().Contain(r =>
            r.Quantity == 7 &&
            r.Request.Contains("forbidden packaged survival meals") &&
            r.ItemDef == "MealSurvivalPack");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "emergency_food_flag");
        advice.Priority.Should().Be(Priority.Critical);
        AdviceByRule(decision, "expand_growing_capacity").Actions.Should().Contain(s => s.Kind == AdviceActionKind.DesignateZone);
        AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(s => s.Kind == AdviceActionKind.PlaceBlueprint);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.Note);
        AgentFlag flag = FlagById(decision, "food:emergency_food_flag");
        flag.Priority.Should().Be(Priority.Critical);
        FlagById(decision, "food:expand_growing_capacity").Attention.Should().Contain(r => r.Request.Contains("growing tiles", StringComparison.OrdinalIgnoreCase));
        FlagById(decision, "food:expand_growing_capacity").BuildingRequests.Should().Contain(r => r.TargetClass == BuildingClass.ProductionBench);
        FlagById(decision, "food:expand_growing_capacity").Attention.Should().Contain(r => r.Request.Contains("growing tiles", StringComparison.OrdinalIgnoreCase));
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "emergency_food_flag");
        advice.Actions.Should().Contain(s =>
            s.Kind == AdviceActionKind.SetStockpileZone &&
            s.Quantity == 46);
        AdviceByRule(decision, "expand_growing_capacity").Actions.Should().Contain(s => s.Kind == AdviceActionKind.DesignateZone);
        AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(s => s.Kind == AdviceActionKind.PlaceBlueprint);
        AgentFlag flag = FlagById(decision, "food:emergency_food_flag");
        flag.BuildingRequests.Should().Contain(r =>
            r.TargetClass == BuildingClass.Stockpile &&
            r.Quantity == 46);
        FlagById(decision, "food:expand_growing_capacity").Attention.Should().Contain(r => r.Request.Contains("growing tiles", StringComparison.OrdinalIgnoreCase));
        FlagById(decision, "food:expand_growing_capacity").BuildingRequests.Should().Contain(r => r.TargetClass == BuildingClass.ProductionBench);
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Contain("harvest_mature_crops");
        AdviceItem advice = AdviceByRule(decision, "harvest_mature_crops");
        AdviceAction Action = advice.Actions.Single();
        Action.Instruction.Should().Contain("nearby to kitchen");
        Action.Apply.Should().NotBeNull();
        Action.Apply!.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        decision.Flags.Should().NotContain(flag => flag.Id == "food:harvest_mature_crops");
    }

    [Fact]
    public void NoCoolerWithStableBuffer_EmitsFreezerMissingAdvice()
    {
        FoodBriefing briefing = Briefing(days: 25f) with
        {
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1)
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Be("freezer_missing");
        AdviceItem advice = AdviceByRule(decision, "freezer_missing");
        advice.Priority.Should().Be(Priority.Medium);
        advice.Actions.Should().ContainSingle().Which.Kind.Should().Be(AdviceActionKind.PlaceBlueprint);
        advice.Actions.Single().Owner.Should().Be("Willie");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should()
            .Contain(r => r.TargetClass == BuildingClass.Freezer && r.RequestedFrom == "Willie");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Contain("harvest_mature_crops");
        AdviceItem advice = AdviceByRule(decision, "harvest_mature_crops");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHarvest);
        AdviceAction freezerAction = AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie").Subject;
        freezerAction.Instruction.Should().Contain("starter freezer");
        freezerAction.Instruction.Should().Contain("next harvest");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.RequestedFrom == "Willie" &&
            request.Request.Contains("starter freezer"));
    }

    [Fact]
    public void MealsUnderstocked_EmitsCookBillStep()
    {
        FoodBriefing briefing = Briefing(days: 12f) with { MealsCount = 1, RawFoodCount = 40, ReadyToHarvest = 0 };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "meals_understocked");
        advice.Actions.Should().Contain(s => s.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(s => s.Kind == AdviceActionKind.RequestResource);
        decision.Flags.Should().NotContain(flag => flag.Id == "food:meals_understocked");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "meals_understocked");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.ProductionBill);
        AdviceAction freezerAction = AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie").Subject;
        freezerAction.Instruction.Should().Contain("raw food and cooked meals");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should().Contain(request =>
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction billAction = AdviceByRule(decision, "meals_understocked")
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction billAction = AdviceByRule(decision, "meals_understocked")
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

        RuleRun result = new Rules().Evaluate(briefing, ColonyContext.Default);

        ProjectedRuleRun decision = Project(result);
        decision.Advice.Should().NotContain(advice => advice.Id == "chef_meals_understocked");
        decision.Advice.SelectMany(advice => advice.Actions).Should()
            .NotContain(action => action.Kind == AdviceActionKind.ProductionBill);
    }

    [Fact]
    public void UrgentShortage_WithSatisfiedSimpleMealBill_DoesNotDuplicateCookAdvice()
    {
        FoodBriefing briefing = Briefing(days: 4f) with
        {
            Kitchen = KitchenWithSimpleMealBill(targetCount: 12)
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "emergency_food_flag");
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.ProductionBill);
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
        decision.Advice.Should().NotContain(item => item.Id == "chef_meals_understocked");
        decision.Flags.SelectMany(flag => flag.LaborRequests ?? []).Should().NotContain(request =>
            request.WorkType == WorkType.Cook);
    }

    [Fact]
    public void UrgentShortage_WithOneCookingWorkbench_AttachesCookBillApply()
    {
        FoodBriefing briefing = Briefing(days: 4f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            Kitchen = new FoodKitchenSummary(1, 1, true, true)
            {
                CookingBuildingIds = ["stove-1"],
                CookingBuildingDetails =
                [
                    new FoodCookingBuildingSummary("stove-1", "FueledStove", "fueled stove", new(93, 0, 186))
                ]
            }
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction billAction = AdviceByRule(decision, "meals_understocked")
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "meals_understocked");
        advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
        FlagById(decision, "food:meals_understocked").LaborRequests.Should().Contain(request =>
            request.WorkType == WorkType.Cook &&
            request.Skill == "Cooking");
    }

    [Fact]
    public void MealsUnderstockedWithoutCookingBuilding_RequestsStarterKitchen()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 1,
            RawFoodCount = 40,
            ReadyToHarvest = 0,
            Kitchen = new FoodKitchenSummary(0, 0, false, false)
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "meals_understocked");
        advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie");
        List<(string FlagId, BuildingRequest Request)> starterKitchenRequests = StarterKitchenRequests(decision);
        starterKitchenRequests.Should().ContainSingle();
        starterKitchenRequests[0].FlagId.Should().Be("food:expand_growing_capacity");
        decision.Flags.Should().NotContain(flag => flag.Id == "food:meals_understocked");
        BuildingRequest request = starterKitchenRequests[0].Request;
        request.TargetClass.Should().Be(BuildingClass.ProductionBench);
        request.TargetDef.Should().Be("Campfire");
        request.RoomClass.Should().Be(RoomClass.Kitchen);
        request.CapacityNeed.Should().NotBeNull();
        request.CapacityNeed!.Measure.Should().Be(CapacityMeasure.WorkSlots);
        request.CapacityNeed.Amount.Should().Be(1);
        request.RequestedFrom.Should().Be("Willie");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "wild_harvest_available");
        advice.Title.Should().Be("Forage can extend the buffer");
        advice.Rationale.Should().Contain("Foraging edible plants");
        AdviceAction action = advice.Actions.Should().ContainSingle(a => a.Kind == AdviceActionKind.MarkHarvest).Subject;
        action.Instruction.Should().Contain("nearest 6 Plant_Berry");
        action.Instruction.Should().NotContain("wild");
        action.Apply.Should().NotBeNull();
        action.Apply!.Label.Should().Be("Mark forage");
        decision.Flags.Should().NotContain(flag => flag.Id == "food:wild_harvest_available");
    }

    [Fact]
    public void WildHarvestWithoutCookingBuilding_RequestsStarterKitchenBeforeFreezer()
    {
        FoodBriefing briefing = Briefing(days: 14f) with
        {
            MealsCount = 57,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 95,
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 95, 1f, "nearby to colonists", "colonist position")],
            HarvestTargets =
            [
                new FoodHarvestTarget("wild", "Plant_Berry", 4, new(80, 121, 83, 149), ["berry-1", "berry-2", "berry-3", "berry-4"], null, "nearby to colonists", "colonist position")
            ],
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 0f, 0),
            Storage = new FoodStorageSummary(0, 0, null, null),
            StockpileCells = 0,
            Kitchen = new FoodKitchenSummary(0, 0, false, false)
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "wild_harvest_available");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHarvest);
        advice.Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie" &&
            action.Instruction.Contains("starter kitchen", StringComparison.OrdinalIgnoreCase));
        AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie" &&
            action.Instruction.Contains("freezer", StringComparison.OrdinalIgnoreCase));
        decision.Flags.Should().NotContain(flag => flag.Id == "food:wild_harvest_available");
        List<(string FlagId, BuildingRequest Request)> starterKitchenRequests = StarterKitchenRequests(decision);
        starterKitchenRequests.Should().ContainSingle();
        starterKitchenRequests[0].FlagId.Should().Be("food:expand_growing_capacity");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.RoomClass == RoomClass.Freezer &&
            request.Adjacency != null &&
            request.Adjacency.Any(hint => string.Equals(hint.Target, "kitchen", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DuplicateStarterKitchenRequests_AreCarriedOnceByDeterministicFirstFlag()
    {
        FoodBriefing briefing = Briefing(days: 12f) with
        {
            MealsCount = 20,
            RawFoodCount = 0,
            ReadyToHarvest = 0,
            WildHarvestCandidates = 6,
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 6, 1f, "nearby to kitchen", "kitchen")],
            HarvestTargets =
            [
                new FoodHarvestTarget("wild", "Plant_Berry", 6, new(30, 40, 32, 41), ["berry-1"], null, "nearby to kitchen", "kitchen")
            ],
            WildAnimalCount = 2,
            WildHuntTargets = [new WildHuntTarget("Hare", 2, "nearby to kitchen", "kitchen")],
            HuntTargets =
            [
                new FoodHuntTarget("Hare", 2, new(40, 50, 41, 50), ["hare-1", "hare-2"], "nearby to kitchen", "kitchen")
            ],
            CropBreakdown = [new FoodCropSummary("Plant_Rice", 36, 0.05f)],
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "2", 36, 0.05f, 0, "nearby to storage")],
            Kitchen = new FoodKitchenSummary(0, 0, false, false)
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Be("rules:hunt_low_risk_animals+wild_harvest_available");
        DecisionShouldIncludeRules(decision, "hunt_low_risk_animals", "wild_harvest_available");
        AdviceByRule(decision, "hunt_low_risk_animals").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie");
        AdviceByRule(decision, "wild_harvest_available").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie");

        List<(string FlagId, BuildingRequest Request)> starterKitchenRequests = StarterKitchenRequests(decision);
        starterKitchenRequests.Should().ContainSingle();
        starterKitchenRequests[0].FlagId.Should().Be("food:hunt_low_risk_animals");

        AgentFlag huntFlag = FlagById(decision, "food:hunt_low_risk_animals");
        huntFlag.BuildingRequests.Should().ContainSingle(request => IsStarterKitchenRequest(request));
        huntFlag.LaborRequests.Should().ContainSingle(request =>
            request.WorkType == WorkType.Hunt &&
            request.Skill == "Shooting");

        decision.Flags.Should().NotContain(flag => flag.Id == "food:wild_harvest_available");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction Action = AdviceByRule(decision, "wild_harvest_available")
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction action = AdviceByRule(decision, "wild_harvest_available")
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "expand_growing_capacity");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));
        Escalate escalation = decision.Effects.OfType<Escalate>().Should().ContainSingle().Subject;

        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.SelectedRule!.Value.Value.Should().Be("unresolved_food_gap");
        decision.Diagnostics.MatchedSignals.Should().NotContain(signal =>
            signal.Rule == "expand_growing_capacity");
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "expand_growing_capacity" &&
            row.Outcome == RuleOutcome.NotMatched &&
            row.Conditions.Contains("already has 36 active matching crop tiles"));
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        DecisionShouldIncludeRules(decision, "emergency_food_flag", "wild_harvest_available", "hunt_low_risk_animals", "freezer_missing");
        AdviceByRule(decision, "wild_harvest_available").Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHarvest);
        AdviceByRule(decision, "hunt_low_risk_animals").Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHunt);
        decision.Advice.Should().NotContain(item => item.Id == "chef_expand_growing_capacity");
        decision.Advice.SelectMany(item => item.Actions).Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);

        decision.Flags.SelectMany(flag => flag.LaborRequests ?? []).Should().NotContain(request => request.WorkType == WorkType.Grow);
        FlagById(decision, "food:hunt_low_risk_animals").LaborRequests.Should().Contain(request =>
            request.WorkType == WorkType.Hunt &&
            request.RequestedFrom == "Labor");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should().Contain(request =>
            request.TargetClass == BuildingClass.Freezer &&
            request.RequestedFrom == "Willie" &&
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction Action = AdviceByRule(decision, "expand_growing_capacity")
            .Actions.Should().ContainSingle().Subject;
        Action.Kind.Should().Be(AdviceActionKind.DesignateZone);
        Action.Instruction.Should().Contain("rice");
        AdviceByRule(decision, "expand_growing_capacity").Body.Should().Contain("1 day of winter margin");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));
        Escalate escalation = decision.Effects.OfType<Escalate>().Should().ContainSingle().Subject;

        escalation.Reason.Should().Contain("Winter is close");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.SelectedRule!.Value.Value.Should().Be("winter_food_tradeoff");
        decision.Diagnostics.MatchedSignals.Should().Contain(signal =>
            signal.Rule == "winter_food_tradeoff" &&
            signal.Outcome == RuleOutcome.Escalated);
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "winter_food_tradeoff" &&
            row.Outcome == RuleOutcome.Escalated &&
            row.Conditions.Contains("d to winter") &&
            row.OutputAction == "");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Contain("hunt_low_risk_animals");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "hunt_targets_blocked_by_risk" &&
            row.Outcome == RuleOutcome.NotMatched &&
            row.Conditions.Contains("low-risk hunt target summaries are visible"));
        AdviceItem advice = AdviceByRule(decision, "hunt_low_risk_animals");
        AdviceAction Action = advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.MarkHunt).Subject;
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
        huntApply.Label.Should().Be("Mark hunt");
        huntApply.TargetIds.Should().Equal("hare-1", "hare-2");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "hunt_low_risk_animals");
        advice.Actions.Should().Contain(action => action.Kind == AdviceActionKind.MarkHunt);
        AdviceAction freezerAction = AdviceByRule(decision, "freezer_missing").Actions.Should().Contain(action =>
            action.Kind == AdviceActionKind.PlaceBlueprint &&
            action.Owner == "Willie").Subject;
        freezerAction.Instruction.Should().Contain("hunted meat");
        FlagById(decision, "food:freezer_missing").BuildingRequests.Should().Contain(request =>
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceAction action = AdviceByRule(decision, "hunt_low_risk_animals")
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        AdviceItem advice = AdviceByRule(decision, "hunt_low_risk_animals");
        advice.Body.Should().Contain("about 14 nutrition");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.MarkHunt).Which.Instruction.Should().Contain("ibex");
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Be("nutrition_signal_gap");
        AdviceItem advice = AdviceByRule(decision, "nutrition_signal_gap");
        advice.Body.Should().Contain("25 food units but no usable meal/raw-food classification");
        advice.Body.Should().NotContain("unknown food units");
        advice.Body.Should().NotContain("audit");
        advice.Actions.Should().ContainSingle().Which.Should().Match<AdviceAction>(action =>
            action.Kind == AdviceActionKind.SetStockpileZone &&
            action.Instruction == "Confirm remaining food units are classified as meals or raw food and reachable before counting them as buffer.");
        decision.Flags.Should().NotContain(flag => flag.Id == "food:nutrition_signal_gap");
    }

    [Fact]
    public void NutritionGapWithForbiddenMeals_LeadsWithUnforbidApplyAndFlagsItemRequest()
    {
        FoodBriefing briefing = Briefing(days: null) with
        {
            FoodUnits = 57,
            MealsCount = 0,
            RawFoodCount = 0,
            NutritionSource = "unknown",
            UnclassifiedFoodItems = [],
            UnforbidTargets = DayOneForbiddenMeals()
        };

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));

        decision.Trace.Should().Be("nutrition_signal_gap");
        AdviceItem advice = AdviceByRule(decision, "nutrition_signal_gap");
        advice.Priority.Should().Be(Priority.Medium);
        advice.Body.Should().Contain("57 forbidden packaged survival meals outside the current food buffer");
        advice.Body.Should().NotContain("unknown food units");
        advice.Body.Should().NotContain("reachable buffer");
        advice.Actions.Should().HaveCount(2);
        AdviceAction unforbidAction = advice.Actions[0];
        unforbidAction.Kind.Should().Be(AdviceActionKind.Unforbid);
        unforbidAction.Quantity.Should().Be(57);
        unforbidAction.Instruction.Should().Be(
            "Unforbid 57 packaged survival meals at (127, 0, 120), (125, 0, 120), (127, 0, 118); then let haulers bring them into the food stockpile.");
        AdviceAction stockpileAction = advice.Actions[1];
        stockpileAction.Kind.Should().Be(AdviceActionKind.SetStockpileZone);
        stockpileAction.Instruction.Should().Be("Confirm remaining food units are classified as meals or raw food and reachable before counting them as buffer.");

        UnforbidThingsApply apply = unforbidAction.Apply.Should().BeOfType<UnforbidThingsApply>().Subject;
        apply.TargetCount.Should().Be(12);
        apply.ThingTargets.Should().HaveCount(12);
        apply.TargetSummary.Should().Be("57 packaged survival meals across 12 stacks");
        apply.ThingTargets.Sum(target => DayOneForbiddenMeals()
            .Single(source => source.Id == target.Id)
            .Count).Should().Be(57);

        AgentFlag flag = FlagById(decision, "food:nutrition_signal_gap");
        flag.Priority.Should().Be(Priority.Medium);
        flag.BuildingRequests.Should().ContainSingle(request =>
            request.TargetClass == BuildingClass.Stockpile &&
            request.RequestedFrom == "Willie");
        ItemRequest itemRequest = flag.ItemRequests.Should().ContainSingle().Subject;
        itemRequest.Request.Should().Be("57 forbidden packaged survival meals");
        itemRequest.Reason.Should().Be("visible meals are forbidden and not counted in the current food buffer");
        itemRequest.ItemDef.Should().Be("MealSurvivalPack");
        itemRequest.Quantity.Should().Be(57);
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

        ProjectedRuleRun decision = Project(new Rules().Evaluate(briefing, ColonyContext.Default));
        Escalate escalation = decision.Effects.OfType<Escalate>().Should().ContainSingle().Subject;

        escalation.Reason.Should().Contain("visible animals");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.SelectedRule!.Value.Value.Should().Be("hunt_targets_blocked_by_risk");
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "hunt_targets_blocked_by_risk" &&
            row.Outcome == RuleOutcome.Escalated &&
            row.Conditions.Contains("risk calculator found no low-risk hunt target") &&
            row.OutputAction == "");
    }

    private static ProjectedRuleRun Project(RuleRun run) =>
        run.ProjectFor("Chef", "food", 1, Briefing(days: 1f).Date, Briefing(days: 1f).GameTick, new DateTimeOffset(2026, 6, 2, 17, 0, 0, TimeSpan.Zero));

    private static AdviceItem AdviceByRule(ProjectedRuleRun decision, string rule) =>
        decision.Advice.Should().ContainSingle(advice => advice.Id == $"chef_{rule}").Subject;

    private static AgentFlag FlagById(ProjectedRuleRun decision, string id) =>
        decision.Flags.Should().ContainSingle(flag => flag.Id == id).Subject;

    private static bool IsStarterKitchenRequest(BuildingRequest request) =>
        request.TargetClass == BuildingClass.ProductionBench &&
        request.RoomClass == RoomClass.Kitchen &&
        string.Equals(request.TargetDef, "Campfire", StringComparison.OrdinalIgnoreCase);

    private static List<(string FlagId, BuildingRequest Request)> StarterKitchenRequests(ProjectedRuleRun decision) =>
        decision.Flags
            .SelectMany(flag => (flag.BuildingRequests ?? [])
                .Where(IsStarterKitchenRequest)
                .Select(request => (flag.Id, request)))
            .ToList();

    private static void DecisionShouldIncludeRules(ProjectedRuleRun decision, params string[] rules)
    {
        foreach (string rule in rules)
            decision.Advice.Should().Contain(advice => advice.Id == $"chef_{rule}");
    }

    internal static FoodBriefing Briefing(float? days) => new(
        BriefingVersion: 1,
        Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
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

    private static IReadOnlyList<FoodUnforbidTarget> DayOneForbiddenMeals() =>
    [
        new("meal-forbidden-01", "MealSurvivalPack", "packaged survival meal x5", 5, "meal", "map_things", new(127, 0, 120)),
        new("meal-forbidden-02", "MealSurvivalPack", "packaged survival meal x5", 5, "meal", "map_things", new(125, 0, 120)),
        new("meal-forbidden-03", "MealSurvivalPack", "packaged survival meal x5", 5, "meal", "map_things", new(127, 0, 118)),
        new("meal-forbidden-04", "MealSurvivalPack", "packaged survival meal x5", 5, "meal", "map_things", new(125, 0, 118)),
        new("meal-forbidden-05", "MealSurvivalPack", "packaged survival meal x5", 5, "meal", "map_things", new(123, 0, 120)),
        new("meal-forbidden-06", "MealSurvivalPack", "packaged survival meal", 5, "meal", "map_things", new(123, 0, 118)),
        new("meal-forbidden-07", "MealSurvivalPack", "packaged survival meal", 5, "meal", "map_things", new(121, 0, 120)),
        new("meal-forbidden-08", "MealSurvivalPack", "packaged survival meal", 5, "meal", "map_things", new(121, 0, 118)),
        new("meal-forbidden-09", "MealSurvivalPack", "packaged survival meal", 5, "meal", "map_things", new(119, 0, 120)),
        new("meal-forbidden-10", "MealSurvivalPack", "packaged survival meal", 4, "meal", "map_things", new(119, 0, 118)),
        new("meal-forbidden-11", "MealSurvivalPack", "packaged survival meal", 4, "meal", "map_things", new(117, 0, 120)),
        new("meal-forbidden-12", "MealSurvivalPack", "packaged survival meal", 4, "meal", "map_things", new(117, 0, 118))
    ];

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
