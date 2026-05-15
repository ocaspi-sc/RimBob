using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State;
using RimAI.State.Derivations;

namespace RimAI.Tests.Food;

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
        b.EstimatedDaysOfFood.Should().BeApproximately(10f, 0.001f);
    }

    [Fact]
    public void Compute_FallsBackToMealAndRawFoodCounts()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 40, 0f, 10, 20, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("fallback_meal_raw_counts");
        b.FallbackNutrition.Should().BeApproximately(10f, 0.001f);
        b.EstimatedDaysOfFood.Should().BeApproximately(6.25f, 0.001f);
        b.UnclassifiedFoodUnits.Should().Be(10);
        b.MissingBriefingSignals.Should().Contain("food_unit_classification");
    }

    [Fact]
    public void Compute_UnknownNutritionLeavesDaysNull()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(0, 0f, 0, 0f, 0, 0, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.NutritionSource.Should().Be("unknown");
        b.EstimatedDaysOfFood.Should().BeNull();
    }

    [Fact]
    public void Compute_UnclassifiedFoodUnits_MakesNutritionGapExplicit()
    {
        ColonyState s = StateWithColonists(1);
        s.Resources.Update(new ResourceSummary(100, 0f, 46, 0f, 0, 0, 0, 0, 0f));

        FoodBriefing b = FoodBriefingDerivation.Compute(s);

        b.EstimatedDaysOfFood.Should().BeNull();
        b.UnclassifiedFoodUnits.Should().Be(46);
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
        b.DataCoverage.HasItemFoodClassification.Should().BeTrue();
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
        b.Skills.BestPlants.Should().Be(10);
        b.Skills.QualifiedCooks.Should().Be(1);
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
            new BuildingRecord("butcher", "ButcherTable", 1f, null, null, new MapPosition(12, 0, 10))
        ]));
        s.Stockpiles.Update(new StockpileLedger([
            new StockpileZone("stock", "StockpileZone", "food", 9, new MapPosition(14, 0, 10))
        ], new Dictionary<string, int>()));
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
        b.WildHarvestClusters.Should().ContainSingle()
            .Which.Proximity.Should().Contain("from kitchen");
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

    private static ColonyState StateWithColonists(int count)
    {
        ColonyState s = new();
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
}
