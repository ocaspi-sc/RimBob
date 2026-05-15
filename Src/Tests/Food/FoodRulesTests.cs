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
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Cook);
        advice.ResourceRequests.Should().NotContain(r => r.Kind == ResourceRequestKind.TradeCapacity);
        decision.Flags.Should().ContainSingle().Which.Severity.Should().Be(FlagSeverity.High);
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
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Tile);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Building);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Labor && r.WorkType == WorkType.Grow);
        advice.ResourceRequests.Should().NotContain(r => r.Kind == ResourceRequestKind.Attention);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.DesignateZone);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.PlaceBlueprint);
        advice.SuggestedActions.Should().NotContain(a => a.Kind == SuggestedActionKind.Note);
        decision.Flags.Should().ContainSingle().Which.Severity.Should().Be(FlagSeverity.Critical);
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
        advice.ResourceRequests.Should().Contain(r =>
            r.Kind == ResourceRequestKind.StockpileSpace &&
            r.Quantity == 46);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Tile);
        advice.ResourceRequests.Should().Contain(r => r.Kind == ResourceRequestKind.Building);
        advice.ResourceRequests.Should().NotContain(r => r.Kind == ResourceRequestKind.Attention);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.SetStockpileZone);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.DesignateZone);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.PlaceBlueprint);
    }

    [Fact]
    public void MatureCrops_EmitsHarvestAdvice()
    {
        FoodBriefing briefing = Briefing(days: 18f) with
        {
            ReadyToHarvest = 9,
            CropZoneSummaries = [new FoodCropZoneSummary("Plant_Rice", "growing:1", 12, 1f, 9, "nearby to kitchen")]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        decision.Trace.Should().Be("harvest_mature_crops");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("harvest_now");
        advice.SuggestedActions.Single().What.Should().Contain("nearby to kitchen");
        advice.ResourceRequests.Should().BeEmpty();
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
        advice.ResourceRequests.Should().NotContain(r => r.Kind == ResourceRequestKind.Labor);
        advice.SuggestedActions.Should().Contain(a => a.Kind == SuggestedActionKind.ProductionBill);
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
        advice.ResourceRequests.Should().Contain(r =>
            r.Kind == ResourceRequestKind.Labor &&
            r.WorkType == WorkType.Cook &&
            r.Skill == "Cooking");
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
            WildHarvestClusters = [new WildHarvestCluster("Plant_Berry", 6, 1f, "nearby to kitchen", "kitchen")]
        };

        Decision decision = new Rules().Evaluate(briefing, ColonyContext.Default)
            .Should().BeOfType<Decision>().Subject;

        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.AdviceType.Should().Be("wild_harvest");
        advice.SuggestedActions.Single().What.Should().Contain("nearest 6 Plant_Berry");
        advice.ResourceRequests.Should().BeEmpty();
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
        ResourceRequest request = advice.ResourceRequests.Should().ContainSingle().Subject;
        request.Kind.Should().Be(ResourceRequestKind.Tile);
        request.Quantity.Should().Be(36);
        request.RequestedFrom.Should().Be("Construction");
        advice.SuggestedActions.Single().Kind.Should().Be(SuggestedActionKind.DesignateZone);
    }

    [Fact]
    public void NutritionGapWithUnclassifiedFoodUnits_RequestsStockpileVisibility()
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
        advice.Body.Should().Contain("25 unclassified food units");
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
