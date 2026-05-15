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
    public async Task Constructor_DoesNotThrow_AndPingReturnsFalse_WhenGeminiKeysMissing()
    {
        var sut = new LlmClient(NullLogger<LlmClient>.Instance);

        sut.IsConfigured.Should().BeFalse();
        sut.ConfiguredKeyCount.Should().Be(0);
        (await sut.PingAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void AdviceNormalizer_MapsSimplifiedGeminiShapeIntoRuntimeContracts()
    {
        const string raw = """
        {
          "summary": "Food is critically low and cooking/freezer paths need attention.",
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
            root => AdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        response.Advice.Should().HaveCount(2);
        response.StateSummary.Should().Be("Food is critically low and cooking/freezer paths need attention.");
        response.Advice[0].Id.Should().StartWith("food_llm_manage_cook_bills_");
        response.Advice[0].Minister.Should().Be("Food");
        response.Advice[0].AdviceType.Should().Be("manage_cook_bills");
        response.Advice[0].Priority.Should().Be(AdvicePriority.High);
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
    public void AdviceNormalizer_MapsLegacyPriorityScoreAndDowngradesVagueLabor()
    {
        const string raw = """
        {
          "advice": [
            {
              "advice_type": "FoodSecurity",
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
            root => AdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        AdviceItem advice = response.Advice.Should().ContainSingle().Subject;
        advice.Priority.Should().Be(AdvicePriority.Critical);
        ResourceRequest request = advice.ResourceRequests.Should().ContainSingle().Subject;
        request.Kind.Should().Be(ResourceRequestKind.Attention);
        request.WorkType.Should().BeNull();
        request.Why.Should().Contain("did not name a RimWorld work type");
    }

    [Fact]
    public void AdviceSchema_RoundTripsPriorityAndResourceWorkMetadata()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        AdviceItem item = new(
            Id: "a1",
            Minister: "Food",
            AdviceType: "manage_cook_bills",
            Priority: AdvicePriority.High,
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
                    Priority: AdvicePriority.High,
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

        serialized.Should().Contain("\"priority\":\"high\"");
        serialized.Should().NotContain("\"priority_score\"");
        serialized.Should().NotContain("\"severity\"");
        serialized.Should().Contain("\"request\":\"Cook work today\"");
        serialized.Should().Contain("\"reason\":\"meals are understocked\"");
        serialized.Should().NotContain("\"what\":\"Cook work today\"");
        serialized.Should().NotContain("\"why\":\"meals are understocked\"");
        serialized.Should().Contain("\"work_type\":\"cook\"");
        roundTripped.Should().NotBeNull();
        roundTripped!.Priority.Should().Be(AdvicePriority.High);
        roundTripped.ResourceRequests.Single().What.Should().Be("Cook work today");
        roundTripped.ResourceRequests.Single().Why.Should().Be("meals are understocked");
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
            new SuggestedAction(SuggestedActionKind.MarkHunt, "mark animals"),
            new SuggestedAction(SuggestedActionKind.PlaceBlueprint, "place stove"),
            new SuggestedAction(SuggestedActionKind.ProductionBill, "cook meals"),
            new SuggestedAction(SuggestedActionKind.SetStockpileZone, "set food stockpile")
        ];

        string serialized = JsonSerializer.Serialize(actions, json);

        serialized.Should().Contain("\"kind\":\"mark_harvest\"");
        serialized.Should().Contain("\"kind\":\"mark_hunt\"");
        serialized.Should().Contain("\"instruction\":\"mark crops\"");
        serialized.Should().Contain("\"kind\":\"place_blueprint\"");
        serialized.Should().Contain("\"kind\":\"production_bill\"");
        serialized.Should().Contain("\"kind\":\"set_stockpile_zone\"");
        serialized.Should().NotContain("\"what\":\"mark crops\"");
    }

    [Fact]
    public void FoodLlmResponseSchema_SerializesStateSummaryBeforeAdvice()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        FoodLlmResponse response = new(
            StateSummary: "Food is stable enough for routine growth.",
            Advice: [],
            Flags: []);

        string serialized = JsonSerializer.Serialize(response, json);

        serialized.Should().StartWith("{\"state_summary\":");
        serialized.Should().Contain("\"advice\":[]");
    }

    [Fact]
    public void FoodLlmResponseParser_StrictResponseStampsRuntimeMetadata()
    {
        const string raw = """
        {
          "state_summary": "Food is in crisis and needs storage visibility plus setup.",
          "advice": [
            {
              "id": "manual_food",
              "minister": "Food",
              "advice_type": "food_security",
              "priority": "high",
              "title": "Set up the food chain",
              "body": "Make storage visible, place cooking, and start growing.",
              "rationale": "reported food units need reachable stockpile visibility.",
              "resource_requests": [
                {
                  "kind": "stockpile_space",
                  "request": "Reachable food stockpile space",
                  "reason": "reported food units need reachable stockpile visibility"
                }
              ],
              "suggested_actions": [],
              "guide_citations": [],
              "issued_at": "5500-04-08T16:00:00Z",
              "expires_at": "5500-04-09T16:00:00Z"
            }
          ],
          "flags": [
            {
              "id": "manual_flag",
              "source_minister": "Food",
              "severity": "high",
              "domain": "food",
              "summary": "Food chain setup needed.",
              "requests": [],
              "detail": "Manual fallback test.",
              "expires_at": "5500-04-09T16:00:00Z"
            }
          ],
          "notes": "food-chain-bootstrap"
        }
        """;
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

        FoodLlmParseResult result = FoodLlmResponseParser.Parse(raw, FoodBriefing(0.21f), []);

        result.ParseMode.Should().Be("strict_json");
        result.Normalized.Should().BeFalse();
        result.Response.StateSummary.Should().Be("Food is in crisis and needs storage visibility plus setup.");
        AdviceItem advice = result.Response.Advice.Should().ContainSingle().Subject;
        advice.ResourceRequests.Should().ContainSingle().Which.What.Should().Be("Reachable food stockpile space");
        advice.IssuedAt.Should().BeAfter(before);
        advice.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        advice.IssuedInGameTick.Should().Be("Y5500AprimayD5");
        advice.BriefingRef.Should().Be(new BriefingRef("Food", 1, "food:1"));
        result.Response.Flags.Should().ContainSingle().Which.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
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
        WildHuntTargets: [],
        StockpileCells: 0,
        Skills: new FoodSkillSnapshot(5, 0, 1, 0),
        Infrastructure: new FoodInfrastructureSnapshot(0, true, 0f, 0),
        Storage: new FoodStorageSummary(0, 0, null, null),
        Kitchen: new FoodKitchenSummary(0, 0, false, false),
        DataCoverage: new FoodDataCoverage(false, false, false, false, false, false)
        {
            HasLiveState = true
        },
        ActiveThreat: false,
        RecentFoodIncidents: []
    );
}
