using FluentAssertions;
using RimBob.Core.Briefings;
using RimBob.Ministers.Food;

namespace RimBob.Tests.Food;

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
            NutritionSource = "item_def_catalog",
            DataCoverage = ClassifiedCoverage(),
            Storage = new FoodStorageSummary(1, 20, null, null)
            {
                PositionedFoodUnits = 40,
                CoolerAdjacentFoodUnits = 40
            },
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

    [Fact]
    public void Recommend_UncertainClassification_ReducesCropScore()
    {
        FoodBriefing trusted = FoodRulesTests.Briefing(16f) with
        {
            FoodUnits = 40,
            MealsCount = 20,
            RawFoodCount = 20,
            NutritionSource = "item_def_catalog",
            DataCoverage = ClassifiedCoverage()
        };
        FoodBriefing uncertain = trusted with
        {
            FoodUnits = 100,
            NutritionSource = "item_category_counts",
            DataCoverage = UnclassifiedCoverage()
        };

        FoodCropCandidate trustedRice = FoodCropMath.Recommend(trusted)
            .Candidates.Single(candidate => candidate.CropDef == "Plant_Rice");
        FoodCropCandidate uncertainRice = FoodCropMath.Recommend(uncertain)
            .Candidates.Single(candidate => candidate.CropDef == "Plant_Rice");

        uncertainRice.ClassificationConfidence.Should().BeLessThan(trustedRice.ClassificationConfidence);
        uncertainRice.Score.Should().BeLessThan(trustedRice.Score);
        uncertainRice.Reason.Should().Contain("buffer classification uncertain");
    }

    [Fact]
    public void Recommend_WeakStorageOnStableBuffer_DownranksCornSurplus()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(30f) with
        {
            NutritionSource = "item_def_catalog",
            DataCoverage = ClassifiedCoverage(),
            Infrastructure = new FoodInfrastructureSnapshot(0, true, 500f, 1),
            Storage = new FoodStorageSummary(1, 20, null, null)
        };

        FoodCropRecommendation recommendation = FoodCropMath.Recommend(briefing);
        FoodCropCandidate corn = recommendation.Candidates.Single(candidate => candidate.CropDef == "Plant_Corn");

        recommendation.BestCandidate.Should().NotBeNull();
        recommendation.BestCandidate!.CropDef.Should().NotBe("Plant_Corn");
        corn.StorageMultiplier.Should().BeLessThan(1f);
        corn.Reason.Should().Contain("storage/freezer posture weak");
    }

    [Fact]
    public void Recommend_UsesDefBackedHarvestNutritionWhenAvailable()
    {
        FoodBriefing briefing = FoodRulesTests.Briefing(30f) with
        {
            NutritionSource = "item_def_catalog",
            DataCoverage = ClassifiedCoverage(),
            CropHarvestNutritionByDef = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["RawCorn"] = 0.08f
            },
            Storage = new FoodStorageSummary(1, 20, null, null)
            {
                PositionedFoodUnits = 40,
                CoolerAdjacentFoodUnits = 40
            },
            Season = new SeasonContext("Aprimay", 12, 40)
        };

        FoodCropCandidate corn = FoodCropMath.Recommend(briefing)
            .Candidates.Single(candidate => candidate.CropDef == "Plant_Corn");

        corn.HarvestNutrition.Should().Be(0.08f);
        corn.ProjectedNutrition.Should().BeApproximately(corn.Tiles * corn.HarvestYield * 0.08f, 0.001f);
        corn.ProjectedDaysAdded.Should().BeGreaterThan(4f);
    }

    private static FoodDataCoverage ClassifiedCoverage() => new(false, false, false, false, false, false)
    {
        HasLiveState = true,
        HasItemFoodClassification = true
    };

    private static FoodDataCoverage UnclassifiedCoverage() => new(false, false, false, false, false, false)
    {
        HasLiveState = true
    };
}
