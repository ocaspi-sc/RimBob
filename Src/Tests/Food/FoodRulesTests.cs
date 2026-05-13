using FluentAssertions;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Ministers.Food;

namespace RimAI.Tests.Food;

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
        advice.Severity.Should().Be(AdviceSeverity.High);
        advice.PriorityScore.Should().Be(AdvicePriorityScore.DefaultForSeverity(AdviceSeverity.High));
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.PlantCut);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Cook);
        decision.Flags.Should().ContainSingle().Which.Severity.Should().Be(FlagSeverity.High);
    }

    [Fact]
    public void MatureCrops_EmitsHarvestAdvice()
    {
        FoodBriefing briefing = Briefing(days: 18f) with { ReadyToHarvest = 9 };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("harvest_mature_crops");
        decision.Advice.Should().ContainSingle().Which.AdviceType.Should().Be("harvest_now");
    }

    [Fact]
    public void MealsUnderstocked_SeparatesResourceRequestsFromActions()
    {
        FoodBriefing briefing = Briefing(days: 12f) with { MealsCount = 1, RawFoodCount = 40, ReadyToHarvest = 0 };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("manage_cook_bills");
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Bill);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Cook && r.Skill == "Cooking");
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.Note);
    }

    [Fact]
    public void NutritionGapWithFoodUnits_EmitsAuditAdvice()
    {
        FoodBriefing briefing = Briefing(days: null) with { FoodUnits = 25, NutritionSource = "unknown" };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("nutrition_signal_gap");
        decision.Advice.Should().ContainSingle().Which.AdviceType.Should().Be("manage_food_stockpile");
    }

    [Fact]
    public void HuntingAmbiguity_Escalates()
    {
        FoodBriefing briefing = Briefing(days: 12f) with { WildAnimalCount = 3, ReadyToHarvest = 0 };

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
        WildHarvestCandidates: 0,
        WildAnimalCount: 0,
        StockpileCells: 20,
        Skills: new FoodSkillSnapshot(10, 1, 8, 1),
        Infrastructure: new FoodInfrastructureSnapshot(1, true, 500f, 1),
        ActiveThreat: false,
        RecentFoodIncidents: []
    );
}
