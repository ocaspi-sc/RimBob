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
            root => LlmAdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        response.Advice.Should().HaveCount(2);
        response.Advice[0].Id.Should().StartWith("food_llm_manage_cook_bills_");
        response.Advice[0].Minister.Should().Be("Food");
        response.Advice[0].AdviceType.Should().Be("manage_cook_bills");
        response.Advice[0].Severity.Should().Be(AdviceSeverity.High);
        response.Advice[0].PriorityScore.Should().Be(AdvicePriorityScore.DefaultForSeverity(AdviceSeverity.High));
        response.Advice[0].Title.Should().Be("Manage Cook Bills");
        response.Advice[0].Body.Should().Contain("Cook simple meals");
        response.Advice[0].ResourceRequests.Should().ContainSingle()
            .Which.Kind.Should().Be(ResourceRequestKind.Labor);
        response.Advice[0].ResourceRequests.Single().WorkType.Should().Be(WorkType.Cook);
        response.Advice[0].ResourceRequests.Single().Skill.Should().Be("Cooking");
        response.Flags.Should().HaveCount(3);
        response.Flags[0].Id.Should().Be("food:food_shortage_critical");
        response.Flags[0].Severity.Should().Be(RimAI.Core.Ministers.FlagSeverity.High);
        response.Flags[2].Summary.Should().Be("No Freezer");
    }

    [Fact]
    public void AdviceNormalizer_ClampsPriorityScoreAndDowngradesVagueLabor()
    {
        const string raw = """
        {
          "advice": [
            {
              "advice_type": "FoodSecurity",
              "severity": "Medium",
              "priority_score": 99,
              "message": "Food needs attention.",
              "resource_requests": [
                {
                  "kind": "labor",
                  "what": "labor capacity",
                  "why": "needs labor capacity"
                }
              ]
            }
          ],
          "flags": [],
          "notes": "Vague labor should not pass through as labor."
        }
        """;
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        FoodBriefing briefing = FoodBriefing(10f);
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
            root => LlmAdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        AdviceItem advice = response.Advice.Should().ContainSingle().Subject;
        advice.PriorityScore.Should().Be(AdvicePriorityScore.Max);
        ResourceRequest request = advice.ResourceRequests.Should().ContainSingle().Subject;
        request.Kind.Should().Be(ResourceRequestKind.Attention);
        request.WorkType.Should().BeNull();
        request.Why.Should().Contain("did not name a RimWorld work type");
    }

    [Fact]
    public void AdviceSchema_RoundTripsPriorityScoreAndResourceWorkMetadata()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        AdviceItem item = new(
            Id: "a1",
            Minister: "Food",
            AdviceType: "manage_cook_bills",
            Severity: AdviceSeverity.High,
            PriorityScore: 9,
            Title: "Cook meals",
            Body: "Body",
            Rationale: "Rationale",
            ResourceRequests:
            [
                new ResourceRequest(
                    ResourceRequestKind.Labor,
                    "Cook work today",
                    "meals are understocked",
                    Quantity: 1,
                    Priority: AdviceSeverity.High,
                    RequestedFrom: "Labor",
                    WorkType: WorkType.Cook,
                    Skill: "Cooking")
            ],
            SuggestedActions: [],
            GuideCitationIds: [],
            IssuedAt: DateTimeOffset.UnixEpoch,
            ExpiresAt: DateTimeOffset.UnixEpoch.AddHours(4));

        string serialized = JsonSerializer.Serialize(item, json);
        AdviceItem? roundTripped = JsonSerializer.Deserialize<AdviceItem>(serialized, json);

        serialized.Should().Contain("\"priority_score\":9");
        serialized.Should().Contain("\"work_type\":\"cook\"");
        roundTripped.Should().NotBeNull();
        roundTripped!.PriorityScore.Should().Be(9);
        roundTripped.ResourceRequests.Single().WorkType.Should().Be(WorkType.Cook);
        roundTripped.ResourceRequests.Single().Skill.Should().Be("Cooking");
    }

    [Fact]
    public void SuggestedActionKinds_SerializeWithSelfDocumentingNames()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        IReadOnlyList<SuggestedAction> actions =
        [
            new SuggestedAction(SuggestedActionKind.MarkHarvest, "mark crops"),
            new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "place stove"),
            new SuggestedAction(SuggestedActionKind.ProductionBill, "cook meals"),
            new SuggestedAction(SuggestedActionKind.SetStockpileZone, "set food stockpile")
        ];

        string serialized = JsonSerializer.Serialize(actions, json);

        serialized.Should().Contain("\"kind\":\"mark_harvest\"");
        serialized.Should().Contain("\"kind\":\"place_blueprint\"");
        serialized.Should().Contain("\"kind\":\"production_bill\"");
        serialized.Should().Contain("\"kind\":\"set_stockpile_zone\"");
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
        CropZoneSummaries: [],
        WildHarvestCandidates: 0,
        WildHarvestClusters: [],
        WildAnimalCount: 0,
        StockpileCells: 0,
        Skills: new FoodSkillSnapshot(5, 0, 1, 0),
        Infrastructure: new FoodInfrastructureSnapshot(0, true, 0f, 0),
        Storage: new FoodStorageSummary(0, 0, null, null),
        Kitchen: new FoodKitchenSummary(0, 0, false, false),
        DataCoverage: new FoodDataCoverage(false, false, false, false, false, false),
        ActiveThreat: false,
        RecentFoodIncidents: []
    );
}
