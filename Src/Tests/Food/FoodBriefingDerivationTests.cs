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
                CurrentJob: null,
                Skills: [new ColonistSkill("Plants", 10, "Major"), new ColonistSkill("Cooking", 8, "Minor")],
                Traits: []))
            .ToList()));
        return s;
    }
}
