using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.State.Derivations;

namespace RimBob.Tests.Food;

public sealed class FoodBriefingDerivationTests
{
    [Fact]
    public void Compute_UsesReportedNutritionWhenPresent()
    {
        ColonyState s = StateWithColonists(2);
        s.Resources.Update(new ResourceSummary(100, 0f, 40, 32f, 10, 20, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("reported");
        b.ReportedNutrition.Should().Be(32f);
        b.FallbackNutrition.Should().BeApproximately(10f, 0.001f);
        b.UsesFallbackNutrition.Should().BeFalse();
        b.EstimatedDaysOfFood.Should().BeApproximately(10f, 0.001f);
        b.MissingBriefingSignals.Should().NotContain(FoodNutrition.MissingSignalRimApiTotalNutritionMissing);
    }

    [Fact]
    public void Compute_FallsBackToMealAndRawFoodCounts()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 40, 0f, 10, 20, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("fallback_meal_raw_counts");
        b.FallbackNutrition.Should().BeApproximately(10f, 0.001f);
        b.UsesFallbackNutrition.Should().BeTrue();
        b.EstimatedDaysOfFood.Should().BeApproximately(6.25f, 0.001f);
        b.UnclassifiedFoodUnits.Should().Be(10);
        b.ExcludedFoodUnits.Should().Be(0);
        b.UnknownFoodUnits.Should().Be(10);
        b.MissingBriefingSignals.Should().Contain(FoodNutrition.MissingSignalRimApiTotalNutritionMissing);
        b.MissingBriefingSignals.Should().Contain("food_unit_classification");
    }

    [Fact]
    public void Compute_FallbackNutritionGapIsExplicitEvenWhenFoodUnitsAreClassified()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 30, 0f, 10, 20, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("fallback_meal_raw_counts");
        b.FallbackNutrition.Should().BeApproximately(10f, 0.001f);
        b.EstimatedDaysOfFood.Should().BeApproximately(6.25f, 0.001f);
        b.UnclassifiedFoodUnits.Should().Be(0);
        b.MissingBriefingSignals.Should().Contain(FoodNutrition.MissingSignalRimApiTotalNutritionMissing);
        b.MissingBriefingSignals.Should().NotContain("food_unit_classification");
    }

    [Fact]
    public void Compute_UnknownNutritionLeavesDaysNull()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(0, 0f, 0, 0f, 0, 0, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("unknown");
        b.UsesFallbackNutrition.Should().BeFalse();
        b.EstimatedDaysOfFood.Should().BeNull();
        b.MissingBriefingSignals.Should().NotContain(FoodNutrition.MissingSignalRimApiTotalNutritionMissing);
    }

    [Fact]
    public void Compute_UnclassifiedFoodUnits_MakesNutritionGapExplicit()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 46, 0f, 0, 0, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.EstimatedDaysOfFood.Should().BeNull();
        b.UnclassifiedFoodUnits.Should().Be(46);
        b.ExcludedFoodUnits.Should().Be(0);
        b.UnknownFoodUnits.Should().Be(46);
        b.MissingBriefingSignals.Should().Contain("food_unit_classification");
    }

    [Fact]
    public void Compute_ItemLevelStoredFood_CatchesLiveSurvivalMealStacks()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 52, 1.64f, 0, 0, 0, 0, 0f));
        s.StoredResources.Update(new StoredResourceRegistry([
            new StoredResourceRecord("food_meals", "meal-1", "MealSurvivalPack", "packaged survival meal", 9, false),
            new StoredResourceRecord("food_meals", "meal-2", "MealSurvivalPack", "packaged survival meal", 9, false),
            new StoredResourceRecord("food_meals", "meal-3", "MealSurvivalPack", "packaged survival meal", 9, false),
            new StoredResourceRecord("food_meals", "meal-4", "MealSurvivalPack", "packaged survival meal", 5, false),
            new StoredResourceRecord("plant_food_raw", "berries-1", "RawBerries", "berries", 12, false)
        ], new Dictionary<string, int>(), new Dictionary<string, int>()));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10),
            ["RawBerries"] = new("RawBerries", "berries", "Item", "ThingWithComps", true, false, false, false, 0.05f, 75)
        }));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("item_def_catalog");
        b.ReportedNutrition.Should().Be(1.64f);
        b.FallbackNutrition.Should().BeApproximately(29.4f, 0.001f);
        b.EstimatedDaysOfFood.Should().BeApproximately(18.375f, 0.001f);
        b.FoodUnits.Should().Be(52);
        b.MealsCount.Should().Be(32);
        b.RawFoodCount.Should().Be(12);
        b.UnclassifiedFoodUnits.Should().Be(8);
        b.ExcludedFoodUnits.Should().Be(0);
        b.UnknownFoodUnits.Should().Be(8);
        b.DataCoverage.HasItemFoodClassification.Should().BeTrue();
    }

    [Fact]
    public void Compute_CarriesKnownCropHarvestNutritionFromThingDefs()
    {
        ColonyState s = StateWithColonists(1);
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["RawRice"] = new("RawRice", "rice", "Item", "ThingWithComps", true, false, false, false, 0.045f, 75),
            ["RawCorn"] = new("RawCorn", "corn", "Item", "ThingWithComps", true, false, false, false, 0.08f, 75),
            ["Steel"] = new("Steel", "steel", "Item", "ThingWithComps", true, false, false, false, 0f, 75)
        }));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.CropHarvestNutritionByDef.Should().Contain("RawRice", 0.045f);
        b.CropHarvestNutritionByDef.Should().Contain("RawCorn", 0.08f);
        b.CropHarvestNutritionByDef.Should().NotContainKey("Steel");
    }

    [Fact]
    public void Compute_StoredFood_ExplainsForbiddenMapFoodRemainder()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 52, 1.64f, 0, 0, 0, 0, 0f));
        s.StoredResources.Update(new StoredResourceRegistry([
            new StoredResourceRecord("food_meals", "meal-1", "MealSurvivalPack", "packaged survival meal", 32, false),
            new StoredResourceRecord("plant_food_raw", "berries-1", "RawBerries", "berries", 12, false)
        ], new Dictionary<string, int>(), new Dictionary<string, int>()));
        s.Things.Update(new ThingRegistry([
            new ThingRecord("stored-meals", "MealSurvivalPack", "packaged survival meal", 32, ["FoodMeals"], false, new MapPosition(89, 0, 189)),
            new ThingRecord("stored-berries", "RawBerries", "berries", 12, ["PlantFoodRaw"], false, new MapPosition(90, 0, 188)),
            new ThingRecord("forbidden-meals", "MealSurvivalPack", "packaged survival meal", 7, ["FoodMeals"], true, new MapPosition(62, 0, 219)),
            new ThingRecord("forbidden-corpse", "Corpse_Squirrel", "squirrel (dead)", 1, ["CorpsesAnimal"], true, new MapPosition(83, 0, 38))
        ]));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10),
            ["RawBerries"] = new("RawBerries", "berries", "Item", "ThingWithComps", true, false, false, false, 0.05f, 75),
            ["Corpse_Squirrel"] = new("Corpse_Squirrel", "squirrel (dead)", "Item", "Corpse", true, false, false, false, 1.04f, 1)
        }));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.MealsCount.Should().Be(32);
        b.RawFoodCount.Should().Be(12);
        b.UnclassifiedFoodUnits.Should().Be(8);
        b.ExcludedFoodUnits.Should().Be(8);
        b.UnknownFoodUnits.Should().Be(0);
        b.UnclassifiedFoodItems.Should().HaveCount(2);
        b.UnclassifiedFoodItems.Should().Contain(item =>
            item.Def == "MealSurvivalPack" &&
            item.Count == 7 &&
            item.Kind == "meal" &&
            item.IsForbidden &&
            item.Position == "(62,0,219)");
        b.UnclassifiedFoodItems.Should().Contain(item =>
            item.Def == "Corpse_Squirrel" &&
            item.Count == 1 &&
            item.Kind == "raw_food" &&
            item.IsForbidden);
        b.MissingBriefingSignals.Should().NotContain("food_unit_classification");
    }

    [Fact]
    public void Compute_DerivesExecutableUnforbidAndHarvestTargetsOnlyFromExactMapData()
    {
        ColonyState s = StateWithColonists(1);
        s.Map.Update(new MapInfoSnapshot(7, "250x250"));
        s.Resources.Update(new ResourceSummary(100, 0f, 39, 0f, 0, 0, 0, 0, 0f));
        s.Things.Update(new ThingRegistry([
            new ThingRecord("forbidden-meal", "MealSurvivalPack", "packaged survival meal", 7, ["FoodMeals"], true, new MapPosition(62, 0, 219)),
            new ThingRecord("forbidden-unpositioned", "MealSurvivalPack", "packaged survival meal", 3, ["FoodMeals"], true),
            new ThingRecord("steel", "Steel", "steel", 75, ["RawResources"], true, new MapPosition(10, 0, 10))
        ]));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10),
            ["Plant_Berry"] = new("Plant_Berry", "berry bush", "Plant", "Plant", false, true, false, false, 0f, null)
        }));
        s.Farm.Update(new FarmSnapshot(2, 1f, 2, [new CropTypeCount("Plant_Berry", 2, 1f, "zone-1", 2)]));
        s.Plants.Update(new PlantRegistry([
            new PlantRecord("berry-1", "Plant_Berry", 0.91f, true, "zone-1", new MapPosition(20, 0, 20)),
            new PlantRecord("berry-2", "Plant_Berry", 0.95f, true, "zone-1", new MapPosition(21, 0, 20)),
            new PlantRecord("berry-unready", "Plant_Berry", 0.40f, true, "zone-1", new MapPosition(22, 0, 20))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.MapId.Should().Be(7);
        FoodUnforbidTarget unforbid = b.UnforbidTargets.Should().ContainSingle().Subject;
        unforbid.Id.Should().Be("forbidden-meal");
        unforbid.Kind.Should().Be("meal");
        FoodHarvestTarget harvest = b.HarvestTargets.Should().ContainSingle(target => target.Source == "crop").Subject;
        harvest.PlantIds.Should().Equal("berry-1", "berry-2");
        harvest.Rect.Should().Be(new MapRect(20, 20, 21, 20));
    }

    [Fact]
    public void Compute_MapThingFallback_IgnoresForbiddenSurvivalMeals()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 39, 0f, 0, 0, 0, 0, 0f));
        s.Things.Update(new ThingRegistry([
            new ThingRecord("forbidden", "MealSurvivalPack", "packaged survival meal", 7, ["FoodMeals"], true),
            new ThingRecord("stored", "MealSurvivalPack", "packaged survival meal", 32, ["FoodMeals"], false)
        ]));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10)
        }));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("item_def_catalog");
        b.MealsCount.Should().Be(32);
        b.RawFoodCount.Should().Be(0);
        b.FallbackNutrition.Should().BeApproximately(28.8f, 0.001f);
        b.ExcludedFoodUnits.Should().Be(7);
        b.UnknownFoodUnits.Should().Be(0);
        b.UnclassifiedFoodItems.Should().ContainSingle(item =>
            item.Def == "MealSurvivalPack" &&
            item.Count == 7 &&
            item.Kind == "meal" &&
            item.IsForbidden);
    }

    [Fact]
    public void Compute_SplitsForbiddenEdibleNutritionFromCurrentFoodBuffer()
    {
        ColonyState forbiddenState = StateWithMealStack(forbidden: true);

        FoodBriefing forbidden = FoodBriefingDerivation.Compute(forbiddenState);

        forbidden.EstimatedDaysOfFood.Should().BeNull();
        forbidden.LatentFoodDays.Should().BeApproximately(5.625f, 0.001f);
        forbidden.MealsCount.Should().Be(0);

        ColonyState unforbiddenState = StateWithMealStack(forbidden: false);

        FoodBriefing unforbidden = FoodBriefingDerivation.Compute(unforbiddenState);

        unforbidden.EstimatedDaysOfFood.Should().BeApproximately(5.625f, 0.001f);
        unforbidden.LatentFoodDays.Should().Be(0f);
        unforbidden.MealsCount.Should().Be(10);
    }

    [Fact]
    public void Compute_DefaultState_MarksLiveStateMissing()
    {
        FoodBriefing b = FoodBriefingDerivation.Compute(new ColonyState());

        b.DataCoverage.HasLiveState.Should().BeFalse();
        b.MissingBriefingSignals.Should().Contain("live_state");
    }

    [Fact]
    public void Compute_SummarizesCropsWildHarvestAnimalsAndSkills()
    {
        ColonyState s = StateWithColonists(1);
        s.Farm.Update(new FarmSnapshot(30, 0.8f, 4, [new CropTypeCount("Rice", 30, 0.8f)]));
        s.Plants.Update(new PlantRegistry([
            new PlantRecord("p1", "BerryBush", 0.9f, false, null),
            new PlantRecord("p2", "Rice", 0.9f, true, "z1")
        ]));
        s.Animals.Update(new AnimalRegistry([
            new AnimalRecord("a1", "Hare", false, 1.0f),
            new AnimalRecord("a2", "Dog", true, 1.0f)
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.ReadyToHarvest.Should().Be(4);
        b.CropBreakdown.Should().ContainSingle().Which.Def.Should().Be("Rice");
        b.WildHarvestCandidates.Should().Be(1);
        b.WildAnimalCount.Should().Be(1);
        b.WildHuntTargets.Should().ContainSingle()
            .Which.Def.Should().Be("Hare");
        b.HuntTargets.Should().BeEmpty();
        b.Skills.BestPlants.Should().Be(10);
        b.Skills.QualifiedCooks.Should().Be(1);
    }

    [Fact]
    public void Compute_ForageHarvest_ExcludesStumpsGrassAndUnharvestablePlants()
    {
        ColonyState s = StateWithColonists(1);
        s.Plants.Update(new PlantRegistry([
            new PlantRecord("stump", "ChoppedStump", 1f, false, null, new MapPosition(1, 0, 1), IsHarvestable: true),
            new PlantRecord("grass", "Plant_Grass", 1f, false, null, new MapPosition(2, 0, 1), IsHarvestable: true),
            new PlantRecord("tall-grass", "Plant_TallGrass", 1f, false, null, new MapPosition(3, 0, 1), IsHarvestable: true),
            new PlantRecord("berry", "Plant_Berry", 1f, false, null, new MapPosition(10, 0, 10), IsHarvestable: true),
            new PlantRecord("agave", "Plant_Agave", 1f, false, null, new MapPosition(11, 0, 10), IsHarvestable: true),
            new PlantRecord("unready-berry", "Plant_Berry", 1f, false, null, new MapPosition(12, 0, 10), IsHarvestable: false)
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.WildHarvestCandidates.Should().Be(2);
        IReadOnlyList<string> clusterDefs = b.WildHarvestClusters.Select(cluster => cluster.Def).ToList();
        clusterDefs.Should().BeEquivalentTo(["Plant_Berry", "Plant_Agave"]);
        clusterDefs.Should().NotContain("ChoppedStump");
        clusterDefs.Should().NotContain("Plant_Grass");
        clusterDefs.Should().NotContain("Plant_TallGrass");
        b.HarvestTargets.Where(target => target.Source == "wild").Select(target => target.Def)
            .Should().BeEquivalentTo(["Plant_Berry", "Plant_Agave"]);
    }

    [Fact]
    public void Compute_ForageHarvest_DerivesBoundedApplyTargetFromLargeCluster()
    {
        ColonyState s = StateWithColonists(1);
        s.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("stove", "FueledStove", 1f, null, null, new MapPosition(10, 0, 10))
        ]));
        List<PlantRecord> plants = Enumerable.Range(0, 111)
            .Select(i => new PlantRecord(
                $"berry-{i}",
                "Plant_Berry",
                1f,
                false,
                null,
                new MapPosition(10 + i % 10, 0, 10 + i / 10),
                IsHarvestable: true))
            .ToList();
        s.Plants.Update(new PlantRegistry(plants));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.WildHarvestCandidates.Should().Be(111);
        FoodHarvestTarget target = b.HarvestTargets.Should()
            .ContainSingle(candidate => candidate.Source == "wild")
            .Subject;
        target.Count.Should().BeGreaterThan(1);
        target.Count.Should().BeLessThanOrEqualTo(AssistedApplyLimits.MaxHarvestTargets);
        target.PlantIds.Should().HaveCount(target.Count);
        target.Rect.Area.Should().BeLessThanOrEqualTo(AssistedApplyLimits.MaxHarvestRectArea);
        target.Proximity.Should().Contain("from kitchen");
    }

    [Fact]
    public void Compute_DerivesThreatAndFreezerSignals()
    {
        ColonyState s = StateWithColonists(1);
        s.Buildings.Update(new BuildingRegistry([new BuildingRecord("b1", "Cooler", 1f, null, null)]));
        s.Power.Update(new PowerNetwork(1000f, 600f, 0f, 0f));
        s.Threats.Update(new ThreatBoard([new HostileLord("l1", "Raid", null, 100f, 3)], []));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.ActiveThreat.Should().BeTrue();
        b.Infrastructure.Coolers.Should().Be(1);
        b.Infrastructure.PowerNetPositive.Should().BeTrue();
    }

    [Fact]
    public void Compute_DerivesCompactSpatialAndOperationalSummaries()
    {
        ColonyState s = StateWithColonists(1);
        s.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("stove", "FueledStove", 1f, null, null, new MapPosition(10, 0, 10)),
            new BuildingRecord("butcher", "ButcherTable", 1f, null, null, new MapPosition(12, 0, 10)),
            new BuildingRecord("cooler", "Cooler", 1f, true, true, new MapPosition(15, 0, 10))
        ]));
        s.Stockpiles.Update(new StockpileLedger([
            new StockpileZone("stock", "StockpileZone", "food", 9, new MapPosition(14, 0, 10))
        ], new Dictionary<string, int>()));
        s.StoredResources.Update(new StoredResourceRegistry([
            new StoredResourceRecord("food_meals", "meal", "MealSurvivalPack", "packaged survival meal", 10, false, new MapPosition(16, 0, 10)),
            new StoredResourceRecord("plant_food_raw", "berries", "RawBerries", "berries", 5, false, new MapPosition(60, 0, 10)),
            new StoredResourceRecord("food_meals", "unplaced-meal", "MealSurvivalPack", "packaged survival meal", 2, false),
            new StoredResourceRecord("food_meals", "forbidden-meal", "MealSurvivalPack", "packaged survival meal", 3, true, new MapPosition(16, 0, 10)),
            new StoredResourceRecord("building_materials", "steel", "Steel", "steel", 75, false, new MapPosition(15, 0, 10))
        ], new Dictionary<string, int>(), new Dictionary<string, int>()));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10),
            ["RawBerries"] = new("RawBerries", "berries", "Item", "ThingWithComps", true, false, false, false, 0.05f, 75)
        }));
        s.Plants.Update(new PlantRegistry([
            new PlantRecord("berry1", "BerryBush", 0.9f, false, null, new MapPosition(18, 0, 10)),
            new PlantRecord("berry2", "BerryBush", 0.95f, false, null, new MapPosition(20, 0, 10)),
            new PlantRecord("rice1", "Rice", 0.86f, true, "zone-rice", new MapPosition(11, 0, 10))
        ]));
        s.Animals.Update(new AnimalRegistry([
            new AnimalRecord("hare1", "Hare", false, 1f, new MapPosition(30, 0, 10))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.Kitchen.HasCookingBuilding.Should().BeTrue();
        b.Kitchen.HasButcherTable.Should().BeTrue();
        b.Storage.NearestKitchenDistanceCells.Should().Be(4);
        b.Storage.NearestKitchenProximity.Should().Contain("from kitchen");
        b.Storage.PositionedFoodUnits.Should().Be(15);
        b.Storage.CoolerAdjacentFoodUnits.Should().Be(10);
        b.Storage.OtherPositionedFoodUnits.Should().Be(5);
        b.Storage.UnpositionedFoodUnits.Should().Be(2);
        b.Storage.HasFoodPlacementSignal.Should().BeTrue();
        b.MissingBriefingSignals.Should().Contain("stored_food_positions");
        b.WildHarvestClusters.Should().ContainSingle()
            .Which.Proximity.Should().Contain("from kitchen");
        b.WildHuntTargets.Should().ContainSingle()
            .Which.Proximity.Should().Contain("from kitchen");
        FoodHuntTarget huntTarget = b.HuntTargets.Should().ContainSingle().Subject;
        huntTarget.AnimalIds.Should().Equal("hare1");
        huntTarget.Rect.Should().Be(new MapRect(30, 10, 30, 10));
        b.CropZoneSummaries.Should().ContainSingle().Which.ReadyCount.Should().Be(1);
        b.DataCoverage.HasPlantPositions.Should().BeTrue();
        b.DataCoverage.HasAnimalPositions.Should().BeTrue();
        b.DataCoverage.HasZoneCells.Should().BeTrue();
        b.DataCoverage.HasBuildingPositions.Should().BeTrue();
        b.DataCoverage.HasLiveState.Should().BeTrue();
        b.DataCoverage.HasWorkPriorities.Should().BeFalse();
        b.DataCoverage.HasTradeAvailability.Should().BeFalse();
        b.UnimplementedBriefingSignals.Should().Contain("work_priorities");
        b.UnimplementedBriefingSignals.Should().Contain("trade_availability");
    }

    [Fact]
    public void Compute_DerivesCookingWorkbenchIdsForAssistedApplyGuard()
    {
        ColonyState s = StateWithColonists(1);
        s.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("stove-1", "FueledStove", 1f, null, null, new MapPosition(10, 0, 10), "fueled stove"),
            new BuildingRecord("campfire-1", "Campfire", 1f, null, null, new MapPosition(11, 0, 10)),
            new BuildingRecord("butcher", "ButcherTable", 1f, null, null, new MapPosition(12, 0, 10))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.Kitchen.CookingBuildings.Should().Be(2);
        b.Kitchen.CookingBuildingIds.Should().Equal("stove-1", "campfire-1");
        b.Kitchen.CookingBuildingDetails.Should().HaveCount(2);
        b.Kitchen.CookingBuildingDetails[0].Label.Should().Be("fueled stove");
        b.Kitchen.CookingBuildingDetails[0].Position.Should().Be(new MapPosition(10, 0, 10));
        b.Kitchen.SingleCookingBuildingId.Should().BeNull();
    }

    [Fact]
    public void Compute_DerivesCookingBillSummariesForFoodRules()
    {
        ColonyState s = StateWithColonists(1);
        s.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("stove-1", "FueledStove", 1f, null, null, new MapPosition(10, 0, 10), "fueled stove")
        ]));
        s.WorkTables.Update(new WorkTableRegistry([
            new WorkTableRecord("stove-1", [
                new WorkTableBillRecord(7, "CookMealSimple", "cook simple meal", false, false, "TargetCount", 1, 12)
            ])
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        FoodCookingBillSummary bill = b.Kitchen.CookingBills.Should().ContainSingle().Subject;
        bill.WorkbenchBuildingId.Should().Be("stove-1");
        bill.RecipeDefName.Should().Be("CookMealSimple");
        bill.TargetCount.Should().Be(12);
        b.Kitchen.SingleCookingBuilding.Should().NotBeNull();
        b.Kitchen.SingleCookingBuilding!.Label.Should().Be("fueled stove");
    }

    [Fact]
    public void Compute_DerivesCompactGrowingTerrainSummary()
    {
        ColonyState s = StateWithColonists(1);
        s.Terrain.Update(new TerrainSnapshot(
            Width: 10,
            Height: 10,
            CellCountsByDef: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoilRich"] = 12,
                ["Soil"] = 48,
                ["Gravel"] = 20,
                ["Limestone_Rough"] = 20
            },
            DefsByName: new Dictionary<string, TerrainDefRecord>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoilRich"] = new("SoilRich", "rich soil", 1.4f, ["GrowSoil", "Walkable"]),
                ["Soil"] = new("Soil", "soil", 1.0f, ["GrowSoil", "Walkable"]),
                ["Gravel"] = new("Gravel", "stony soil", 0.7f, ["GrowSoil", "Walkable"]),
                ["Limestone_Rough"] = new("Limestone_Rough", "rough limestone", 0f, ["Walkable"])
            }));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.GrowingTerrain.HasTerrain.Should().BeTrue();
        b.GrowingTerrain.GrowableCells.Should().Be(80);
        b.GrowingTerrain.BestFertility.Should().Be(1.4f);
        b.GrowingTerrain.AverageFertility.Should().BeApproximately(0.985f, 0.001f);
        b.GrowingTerrain.FertilityBands.Select(band => band.Def)
            .Should().Equal("SoilRich", "Soil", "Gravel");
        b.DataCoverage.HasTerrainFertility.Should().BeTrue();
    }

    [Fact]
    public void Compute_UsesFarmCropZoneDataWhenPlantZoneDetailsAreMissing()
    {
        ColonyState s = StateWithColonists(1);
        s.Farm.Update(new FarmSnapshot(
            TotalCrops: 36,
            AverageGrowth: 0.05f,
            ReadyToHarvest: 0,
            CropBreakdown: [new CropTypeCount("Plant_Rice", 36, 0.05f, "2", 0)]));
        s.Plants.Update(new PlantRegistry([
            new PlantRecord("44187", "Plant_Rice", 0f, true, null, new MapPosition(85, 0, 190))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        FoodCropZoneSummary crop = b.CropZoneSummaries.Should().ContainSingle().Which;
        crop.Def.Should().Be("Plant_Rice");
        crop.ZoneId.Should().Be("2");
        crop.Count.Should().Be(36);
        crop.AverageGrowth.Should().BeApproximately(0.05f, 0.001f);
        crop.ReadyCount.Should().Be(0);
    }

    [Fact]
    public void Compute_HuntingTargetsExcludeDangerousAnimals()
    {
        ColonyState s = StateWithColonists(1);
        s.Animals.Update(new AnimalRegistry([
            new AnimalRecord("a1", "Wolf", false, 1f, new MapPosition(30, 0, 10)),
            new AnimalRecord("a2", "Hare", false, 1f, new MapPosition(20, 0, 10)),
            new AnimalRecord("a3", "Dog", true, 1f, new MapPosition(15, 0, 10))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.WildAnimalCount.Should().Be(2);
        b.WildHuntTargets.Should().ContainSingle()
            .Which.Def.Should().Be("Hare");
        b.HuntTargets.Should().ContainSingle()
            .Which.AnimalIds.Should().Equal("a2");
    }

    [Fact]
    public void Compute_HuntingTargetsPreferHigherValueLowRiskGroups()
    {
        ColonyState s = StateWithColonists(1);
        s.AnimalDefs.Update(new AnimalDefRegistry(new Dictionary<string, AnimalDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hare"] = AnimalDef("Hare", bodySize: 0.2f, nutrition: 1.4f),
            ["Ibex"] = AnimalDef("Ibex", bodySize: 0.45f, nutrition: 3.5f)
        }));
        s.Animals.Update(new AnimalRegistry([
            new AnimalRecord("hare-1", "Hare", false, 1f, new MapPosition(10, 0, 10)),
            new AnimalRecord("ibex-1", "Ibex", false, 1f, new MapPosition(12, 0, 10)),
            new AnimalRecord("ibex-2", "Ibex", false, 1f, new MapPosition(13, 0, 10)),
            new AnimalRecord("ibex-3", "Ibex", false, 1f, new MapPosition(14, 0, 10)),
            new AnimalRecord("ibex-4", "Ibex", false, 1f, new MapPosition(15, 0, 10))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.WildHuntTargets.Should().NotBeEmpty();
        WildHuntTarget summary = b.WildHuntTargets[0];
        summary.Def.Should().Be("Ibex");
        summary.EstimatedNutrition.Should().BeApproximately(14f, 0.001f);
        summary.ScoreReason.Should().Contain("about 14 nutrition");
        b.HuntTargets.Should().NotBeEmpty();
        FoodHuntTarget applyTarget = b.HuntTargets[0];
        applyTarget.Def.Should().Be("Ibex");
        applyTarget.Count.Should().Be(2);
        applyTarget.EstimatedNutrition.Should().BeApproximately(7f, 0.001f);
    }

    [Fact]
    public void Compute_HuntingRiskSummariesExposeDangerousMetadata()
    {
        ColonyState s = StateWithColonists(1);
        s.AnimalDefs.Update(new AnimalDefRegistry(new Dictionary<string, AnimalDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["CuteFox"] = AnimalDef("CuteFox", bodySize: 0.45f, nutrition: 2.0f, predator: true)
        }));
        s.Animals.Update(new AnimalRegistry([
            new AnimalRecord("fox-1", "CuteFox", false, 1f, new MapPosition(10, 0, 10))
        ]));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.WildAnimalCount.Should().Be(1);
        b.WildHuntTargets.Should().BeEmpty();
        FoodHuntRiskSummary risk = b.HuntRiskSummaries.Should().ContainSingle().Subject;
        risk.Def.Should().Be("CuteFox");
        risk.Risk.Should().Be("dangerous");
        risk.Reason.Should().Contain("predator");
    }

    private static ColonyState StateWithColonists(int count)
    {
        ColonyState s = new();
        s.LastRefreshSource = ColonyStateOrigin.Live;
        s.LastLiveRefreshAt = DateTimeOffset.UtcNow;
        s.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, "5th of Aprimay, 5500, 14h"));
        s.Colonists.Update(new ColonistRegistry(Enumerable.Range(0, count)
            .Select(i => new ColonistRecord(
                Id: $"p{i}",
                Name: $"P{i}",
                Age: 30,
                Gender: "Female",
                Health: 1f,
                Mood: 0.7f,
                Hunger: 1f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [new ColonistSkill("Plants", 10, "Major"), new ColonistSkill("Cooking", 8, "Minor")],
                Traits: []))
            .ToList()));
        return s;
    }

    private static ColonyState StateWithMealStack(bool forbidden)
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 10, 0f, 0, 0, 0, 0, 0f));
        s.Things.Update(new ThingRegistry([
            new ThingRecord(
                "meal-stack",
                "MealSurvivalPack",
                "packaged survival meal",
                10,
                ["FoodMeals"],
                forbidden,
                new MapPosition(62, 0, 219))
        ]));
        s.ThingDefs.Update(new ThingDefRegistry(new Dictionary<string, ThingDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["MealSurvivalPack"] = new("MealSurvivalPack", "packaged survival meal", "Item", "ThingWithComps", true, false, false, false, 0.9f, 10)
        }));
        return s;
    }

    private static AnimalDefRecord AnimalDef(
        string def,
        float bodySize,
        float nutrition,
        bool predator = false) =>
        new(
            Def: def,
            Label: def,
            BodySize: bodySize,
            HealthScale: 1f,
            Predator: predator,
            HerdAnimal: false,
            PackAnimal: false,
            IsInsect: false,
            Explosive: false,
            ManhunterOnDamageChance: 0f,
            Wildness: 0.5f,
            MeatAmount: nutrition / 0.05f,
            EstimatedMeatNutrition: nutrition,
            LeatherAmount: 0f,
            LeatherDef: null,
            Petness: 0f);
}
