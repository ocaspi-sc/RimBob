using FluentAssertions;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Ministers.Mayor;

namespace RimAI.Tests.Mayor;

public sealed class MayorAgendaRulesTests
{
    [Fact]
    public void WinterPrepDirective_FiresWhenDaysToWinterUnder20()
    {
        MayorBriefing briefing = BriefingBuilder.Default with { Season = new SeasonContext("Septober", 8, 18) };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.WinterPrepRequired.Should().BeTrue();
        directives.Directives.Should().Contain(p => p.Contains("Winter prep"));
    }

    [Fact]
    public void WinterPrepDirective_DoesNotFireWhenWinterFar()
    {
        MayorBriefing briefing = BriefingBuilder.Default with { Season = new SeasonContext("Aprimay", 12, 45) };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.WinterPrepRequired.Should().BeFalse();
    }

    [Fact]
    public void FoodSecurityDirective_FiresWhenDaysOfFoodUnder7()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Food = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = 4.2f }
        };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.FoodSecurityCritical.Should().BeTrue();
        directives.Directives.Should().Contain(p => p.Contains("Food security"));
    }

    [Fact]
    public void FoodSecurityDirective_DoesNotFireWhenDaysOfFoodIsNull()
    {
        // Current state — stockpile endpoint not wired yet, EstimatedDaysOfFood derives null.
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Food = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = null }
        };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.FoodSecurityCritical.Should().BeFalse();
    }

    [Fact]
    public void YearTwoTransition_FiresOnY2Q1()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Date = new DateStamp("2nd of Aprimay, 5501", 2, "Q1", 2, 6)
        };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.YearTwoTransition.Should().BeTrue();
    }

    [Fact]
    public void QuietDayDirective_EmittedWhenNoOtherDirectiveFires()
    {
        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(BriefingBuilder.Default, ColonyContext.Default);

        directives.QuietDay.Should().BeTrue();
        directives.WinterPrepRequired.Should().BeFalse();
        directives.FoodSecurityCritical.Should().BeFalse();
        directives.YearTwoTransition.Should().BeFalse();
        directives.Directives.Should().Contain(p => p.Contains("Quiet day"));
    }

    [Fact]
    public void Directives_AccumulateAcrossRules()
    {
        MayorBriefing briefing = BriefingBuilder.Default with
        {
            Season = new SeasonContext("Septober", 8, 12),
            Food   = BriefingBuilder.Default.Food with { EstimatedDaysOfFood = 5f }
        };

        MayorDirectiveSet directives = new MayorAgendaRules().Evaluate(briefing, ColonyContext.Default);

        directives.WinterPrepRequired.Should().BeTrue();
        directives.FoodSecurityCritical.Should().BeTrue();
        directives.Directives.Count.Should().Be(2);
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
