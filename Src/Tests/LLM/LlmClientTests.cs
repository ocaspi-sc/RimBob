using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.LLM;

namespace RimAI.Tests.LLM;

[Collection(nameof(LlmClientTestCollection))]
public sealed class LlmClientTests
{
    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenExecutorSucceeds()
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromResult<string?>("pong"));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PingAsync_ReturnsFalse_WhenReplyMissingOrWhitespace(string? reply)
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromResult<string?>(reply));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenExecutorThrows()
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromException<string?>(new InvalidOperationException("boom")));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_DoesNotThrow_AndPingReturnsFalse_WhenGeminiKeyMissing()
    {
        const string keyName = "GEMINI_API_KEY";
        var previous = Environment.GetEnvironmentVariable(keyName);
        try
        {
            Environment.SetEnvironmentVariable(keyName, null);

            var sut = new LlmClient(NullLogger<LlmClient>.Instance);

            sut.IsConfigured.Should().BeFalse();
            (await sut.PingAsync(CancellationToken.None)).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyName, previous);
        }
    }

    [Fact]
    public void AdviceNormalizer_MapsSimplifiedGeminiShapeIntoRuntimeContracts()
    {
        const string raw = """
        {
          "advice": [
            {
              "advice_type": "ManageCookBills",
              "severity": "High",
              "message": "Your colony has 0 meals and only 0.21 days of food remaining. Cook simple meals immediately.",
              "resource_requests": [
                {
                  "resource_type": "labor_capacity",
                  "amount": 1,
                  "unit": "pawn_hours",
                  "description": "for cooking meals"
                }
              ]
            },
            {
              "advice_type": "ManageFreezer",
              "severity": "High",
              "message": "Your colony has no coolers. Prioritize building at least one cooler.",
              "resource_requests": [
                {
                  "resource_type": "components",
                  "amount": 1,
                  "unit": "units",
                  "description": "for building a cooler"
                }
              ]
            }
          ],
          "flags": [
            "FoodShortageCritical",
            "NoMeals",
            "NoFreezer"
          ],
          "notes": "Immediate starvation risk due to 0 meals and very low food supply."
        }
        """;
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        FoodBriefing briefing = FoodBriefing(0.21f);

        LlmAdviceNormalizationContext context = new(
            Minister: "Food",
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameTick: briefing.GameTick,
            Date: briefing.Date,
            DefaultAdviceType: nameof(FoodAdviceType.FoodSecurity),
            DefaultRationale: "Food LLM escalation selected this recommendation.",
            GuideContext: []);

        NormalizedAdviceResponse response = LlmResponseParser.ParseOrNormalize(
            raw,
            json,
            root => LlmAdviceResponseNormalizer.Normalize(root, context, json));

        response.Advice.Should().HaveCount(2);
        response.Advice[0].Id.Should().StartWith("food_llm_manage_cook_bills_");
        response.Advice[0].Minister.Should().Be("Food");
        response.Advice[0].AdviceType.Should().Be("manage_cook_bills");
        response.Advice[0].Severity.Should().Be(AdviceSeverity.High);
        response.Advice[0].Title.Should().Be("Manage Cook Bills");
        response.Advice[0].Body.Should().Contain("Cook simple meals");
        response.Advice[0].ResourceRequests.Should().ContainSingle()
            .Which.Kind.Should().Be(ResourceRequestKind.Labor);
        response.Flags.Should().HaveCount(3);
        response.Flags[0].Id.Should().Be("food:food_shortage_critical");
        response.Flags[0].Severity.Should().Be(RimAI.Core.Ministers.FlagSeverity.High);
        response.Flags[2].Summary.Should().Be("No Freezer");
    }

    private static FoodBriefing FoodBriefing(float days) => new(
        BriefingVersion: 1,
        Date: new DateStamp("5th of Aprimay, 5500, 14h", 5500, "Aprimay", 5, 14),
        GameTick: 300_000,
        Season: new SeasonContext("Aprimay", 11, 50),
        ColonistCount: 3,
        ReportedNutrition: days * 4.8f,
        FallbackNutrition: null,
        NutritionSource: "reported",
        EstimatedDaysOfFood: days,
        FoodUnits: 60,
        MealsCount: 0,
        RawFoodCount: 0,
        ReadyToHarvest: 0,
        CropBreakdown: [],
        WildHarvestCandidates: 0,
        WildAnimalCount: 0,
        StockpileCells: 0,
        Skills: new FoodSkillSnapshot(5, 0, 1, 0),
        Infrastructure: new FoodInfrastructureSnapshot(0, true, 0f, 0),
        ActiveThreat: false,
        RecentFoodIncidents: []
    );
}
