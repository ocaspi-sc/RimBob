using FluentAssertions;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Ministers.Mayor;

namespace RimAI.Tests.Mayor;

public sealed class MayorRulesTests
{
    [Fact]
    public void WinterPrepLens_FiresWhenDaysToWinterUnder20()
    {
        MayorBriefing briefing = BriefingBuilder.Default with { Season = new SeasonContext("Septober", 8, 18) };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.WinterPrep.Should().BeTrue();
        lens.PromptPrefills.Should().Contain(p => p.Contains("Winter prep"));
    }

    [Fact]
    public void WinterPrepLens_DoesNotFireWhenWinterFar()
    {
        MayorBriefing briefing = BriefingBuilder.Default with { Season = new SeasonContext("Aprimay", 12, 45) };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.WinterPrep.Should().BeFalse();
    }

    [Fact]
    public void FoodCrisisLens_FiresWhenDaysOfFoodUnder7()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Food = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = 4.2f }
        };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.FoodCrisis.Should().BeTrue();
        lens.PromptPrefills.Should().Contain(p => p.Contains("Food crisis"));
    }

    [Fact]
    public void FoodCrisisLens_DoesNotFireWhenDaysOfFoodIsNull()
    {
        // Current state — stockpile endpoint not wired yet, EstimatedDaysOfFood derives null.
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Food = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = null }
        };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.FoodCrisis.Should().BeFalse();
    }

    [Fact]
    public void YearTwoTransition_FiresOnY2Q1()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Date = new DateStamp("2nd of Aprimay, 5501", 2, "Q1", 2, 6)
        };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.YearTwoTransition.Should().BeTrue();
    }

    [Fact]
    public void QuietDay_PrefillEmittedWhenNoOtherLensFires()
    {
        MayorLensSet lens = new MayorRules().Evaluate(BriefingBuilder.Default, ColonyContext.Default);

        lens.QuietDay.Should().BeTrue();
        lens.WinterPrep.Should().BeFalse();
        lens.FoodCrisis.Should().BeFalse();
        lens.YearTwoTransition.Should().BeFalse();
        lens.PromptPrefills.Should().Contain(p => p.Contains("Quiet day"));
    }

    [Fact]
    public void PromptPrefills_AccumulatesAcrossLenses()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Season = new SeasonContext("Septober", 8, 12),
            Food   = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = 5f }
        };

        MayorLensSet lens = new MayorRules().Evaluate(briefing, ColonyContext.Default);

        lens.WinterPrep.Should().BeTrue();
        lens.FoodCrisis.Should().BeTrue();
        lens.PromptPrefills.Count.Should().Be(2);
    }
}

internal static class BriefingBuilder
{
    public static MayorBriefing Default { get; } = new(
        BriefingVersion: 1,
        Date:            new DateStamp("5th of Aprimay, 5500, 14h", 1, "Aprimay", 5, 14),
        GameTick:        300_000,
        Season:          new SeasonContext("Aprimay", 11, 50),
        Colonists:       new ColonistsSummary(0, 0, 0, [], null),
        Skills:          new SkillCoverage(new Dictionary<string, SkillCoverageEntry>()),
        Traits:          new TraitCallouts([], []),
        Medical:         new MedicalState(0, 0, 0),
        Prisoners:       0,
        Food:            new FoodSnapshot(0, 0f, 0, [], 0, 30f),
        Resources:       new ResourceSnapshot(
                             new Dictionary<string, int>(),
                             new Dictionary<string, int>(),
                             new Dictionary<string, int>()),
        Power:           new PowerSnapshot(0f, 0f, 0f, 0f, 0f),
        Buildings:       new BuildingsSummary(0, 0, 0, new Dictionary<string, int>()),
        Mood:            new MoodSnapshot(0.7f, 0, 0, 0),
        Threat:          new ThreatSnapshot(false, 0, 0f, [], []),
        Wealth:          new WealthSnapshot(0f, 0, 0f),
        Weather:         new WeatherSnapshot("Clear", 15f, 0f),
        Research:        new ResearchSnapshot(null, null)
    );
}
