using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;

namespace RimBob.Tests.Food;

public sealed class FoodHuntSafetyTests
{
    [Fact]
    public void Diagnose_WhenAnimalIsTame_BlocksBeforeMetadataLookup()
    {
        HuntRiskDiagnostic diagnostic = FoodHuntSafety.Diagnose(
            new AnimalRecord("alpaca-1", "Alpaca", true, 1f),
            EmptyAnimalDefs());

        diagnostic.Profile.Risk.Should().Be("blocked");
        diagnostic.Profile.IsLowRisk.Should().BeFalse();
        diagnostic.MetadataAvailable.Should().BeFalse();
        diagnostic.Signals.Should().Contain(signal =>
            signal.Key == "eligibility" &&
            signal.Tone == "blocked" &&
            signal.Value.Contains("tame"));
    }

    [Fact]
    public void Diagnose_WhenMetadataPredator_ReturnsDangerousSignal()
    {
        AnimalDefRegistry defs = AnimalDefs(AnimalDef("CuteFox", predator: true, nutrition: 2f));

        HuntRiskDiagnostic diagnostic = FoodHuntSafety.Diagnose(
            new AnimalRecord("fox-1", "CuteFox", false, 1f),
            defs);

        diagnostic.MetadataAvailable.Should().BeTrue();
        diagnostic.FallbackUsed.Should().BeFalse();
        diagnostic.Profile.Risk.Should().Be("dangerous");
        diagnostic.Profile.IsLowRisk.Should().BeFalse();
        diagnostic.Profile.EstimatedNutrition.Should().Be(2f);
        diagnostic.Signals.Should().Contain(signal => signal.Key == "predator" && signal.Tone == "dangerous");
    }

    [Fact]
    public void Diagnose_WhenMetadataMissing_ReportsFallbackPath()
    {
        HuntRiskDiagnostic diagnostic = FoodHuntSafety.Diagnose(
            new AnimalRecord("hare-1", "Hare", false, 1f),
            EmptyAnimalDefs());

        diagnostic.MetadataAvailable.Should().BeFalse();
        diagnostic.FallbackUsed.Should().BeTrue();
        diagnostic.Profile.Risk.Should().Be("low");
        diagnostic.Profile.RiskRank.Should().Be(0);
        diagnostic.Signals.Should().Contain(signal => signal.Key == "fallback" && signal.Tone == "low");
    }

    private static AnimalDefRegistry EmptyAnimalDefs() =>
        new(new Dictionary<string, AnimalDefRecord>(StringComparer.OrdinalIgnoreCase));

    private static AnimalDefRegistry AnimalDefs(params AnimalDefRecord[] defs) =>
        new(defs.ToDictionary(def => def.Def, StringComparer.OrdinalIgnoreCase));

    private static AnimalDefRecord AnimalDef(
        string def,
        bool predator = false,
        float nutrition = 0f) =>
        new(
            Def: def,
            Label: def,
            BodySize: 0.4f,
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
