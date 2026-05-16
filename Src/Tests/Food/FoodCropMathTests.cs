using FluentAssertions;
using RimAI.Core.Briefings;
using RimAI.Ministers.Food;

namespace RimAI.Tests.Food;

public sealed class FoodCropMathTests
{
    [Fact]
    public void Recommend_LowBufferWithLongSeason_RanksRiceFirst()
    {
        FoodCropRecommendation recommendation = FoodCropMath.Recommend(FoodRulesTests.Briefing(4f));

        recommendation.BestCandidate.Should().NotBeNull();
        recommendation.BestCandidate!.CropDef.Should().Be("Plant_Rice");
        recommendation.BestCandidate.FitsSeason.Should().BeTrue();
        recommendation.BestCandidate.ProjectedDaysAdded.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void Recommend_NearWinterRejectsCornButKeepsRiceWhenItFits()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(10f) with
        {
            Season = new SeasonContext("Decembary", 2, 5)
        };

        FoodCropRecommendation recommendation = FoodCropMath.Recommend(briefing);

        recommendation.BestCandidate.Should().NotBeNull();
        recommendation.BestCandidate!.CropDef.Should().Be("Plant_Rice");
        recommendation.Candidates.Single(candidate => candidate.CropDef == "Plant_Corn")
            .FitsSeason.Should().BeFalse();
    }

    [Fact]
    public void Recommend_TooCloseToWinterHasNoSeasonFit()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(10f) with
        {
            Season = new SeasonContext("Decembary", 2, 2)
        };

        FoodCropRecommendation recommendation = FoodCropMath.Recommend(briefing);

        recommendation.BestCandidate.Should().BeNull();
        recommendation.Candidates.Should().OnlyContain(candidate => candidate.FitsSeason == false);
    }

    [Fact]
    public void Recommend_StableBufferOnRichSoil_RanksCornForNutrition()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(30f) with
        {
            Season = new SeasonContext("Aprimay", 12, 40),
            GrowingTerrain = new FoodGrowingTerrainSummary(
                HasTerrain: true,
                GrowableCells: 72,
                BestFertility: 1.4f,
                AverageFertility: 1.4f,
                FertilityBands: [new FoodTerrainFertilityBand("SoilRich", "rich soil", 1.4f, 72)])
        };

        FoodCropRecommendation recommendation = FoodCropMath.Recommend(briefing);

        recommendation.BestCandidate.Should().NotBeNull();
        recommendation.BestCandidate!.CropDef.Should().Be("Plant_Corn");
        recommendation.BestCandidate.TerrainFertility.Should().Be(1.4f);
        recommendation.BestCandidate.GrowDays.Should().BeLessThan(recommendation.BestCandidate.BaseGrowDays);
        recommendation.BestCandidate.Reason.Should().Contain("fertility 1.4");
    }
}
