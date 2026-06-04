using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.LLM;

namespace RimBob.Tests.LLM;

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
              "priority": "High",
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
              "priority": "High",
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
            Minister: "Chef",
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameTick: briefing.GameTick,
            Date: briefing.Date,
            DefaultRationale: "Chef LLM escalation selected this recommendation.",
            GuideContext: []);

        NormalizedAdviceResponse response = LlmResponseParser.ParseOrNormalize(
            raw,
            json,
            root => AdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        response.Advice.Should().HaveCount(2);
        response.StateSummary.Should().Be("Food is critically low and cooking/freezer paths need attention.");
        response.Advice[0].Id.Should().Be("food_llm_300000_1");
        response.Advice[0].Minister.Should().Be("Chef");
        response.Advice[0].Priority.Should().Be(Priority.High);
        response.Advice[0].Title.Should().Be("1 pawn_hours labor capacity");
        response.Advice[0].Body.Should().Contain("Cook simple meals");
        response.Advice[0].Actions.Should().ContainSingle()
            .Which.Kind.Should().Be(AdviceActionKind.RequestResource);
        response.Advice[0].Actions.Single().WorkType.Should().Be(WorkType.Cook);
        response.Advice[0].Actions.Single().Skill.Should().Be("Cooking");
        response.Flags.Should().HaveCount(3);
        response.Flags[0].Id.Should().Be("food:food_shortage_critical");
        response.Flags[0].Priority.Should().Be(Priority.High);
        response.Flags[2].Summary.Should().Be("No Freezer");
    }

    [Fact]
    public void AdviceNormalizer_MapsLegacyPriorityScoreAndDowngradesVagueLabor()
    {
        const string raw = """
        {
          "advice": [
            {
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
            Minister: "Chef",
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameTick: briefing.GameTick,
            Date: briefing.Date,
            DefaultRationale: "Chef LLM escalation selected this recommendation.",
            GuideContext: []);

        NormalizedAdviceResponse response = LlmResponseParser.ParseOrNormalize(
            raw,
            json,
            root => AdviceResponseNormalizer.Normalize(root, context, json),
            isStrictValid: _ => false);

        AdviceItem advice = response.Advice.Should().ContainSingle().Subject;
        advice.Priority.Should().Be(Priority.Critical);
        AdviceAction action = advice.Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.RequestResource);
        action.Instruction.Should().Be("labor capacity");
        action.WorkType.Should().BeNull();
    }

    [Fact]
    public void AdviceSchema_RoundTripsPriorityActionAndOptionMetadata()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        AdviceItem item = new(
            Id: "a1",
            Minister: "Chef",
            Priority: Priority.High,
            Title: "Cook meals",
            Body: "Body",
            Rationale: "Rationale",
            Actions:
            [
                new AdviceAction(
                    AdviceActionKind.SetPriority,
                    "Put the best cook on Cook work today",
                    Quantity: 1,
                    Owner: "Labor",
                    WorkType: WorkType.Cook,
                    Skill: "Cooking")
            ],
            GuideCitationIds: [],
            Stamp: new AdviceStamp(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(4)),
            Options:
            [
                new AdviceOption(
                    Id: "compact_freezer",
                    Label: "Compact freezer",
                    Summary: "Small freezer near the kitchen.",
                    BlueprintGroup: new BlueprintGroup(
                        Label: "Compact freezer",
                        MapId: 1,
                        Assets:
                        [
                            new BlueprintAsset(
                                Role: "wall",
                                DefName: "Wall",
                                StuffDefName: "BlocksGranite",
                                Cell: new MapCell(12, 34),
                                Rotation: 0)
                        ]),
                    EstimatedMaterials: [new MaterialEstimate("BlocksGranite", 5)],
                    TradeoffNote: "cheap but tight")
            ]);

        string serialized = JsonSerializer.Serialize(item, json);
        AdviceItem? roundTripped = JsonSerializer.Deserialize<AdviceItem>(serialized, json);

        serialized.Should().Contain("\"priority\":\"high\"");
        serialized.Should().NotContain("\"priority_score\"");
        serialized.Should().NotContain("\"severity\"");
        serialized.Should().Contain("\"actions\":[");
        serialized.Should().Contain("\"instruction\":\"Put the best cook on Cook work today\"");
        serialized.Should().NotContain("\"reason\"");
        serialized.Should().Contain("\"owner\":\"Labor\"");
        serialized.Should().NotContain("\"resource_requests\"");
        serialized.Should().NotContain("\"suggested_actions\"");
        serialized.Should().NotContain("\"what\":\"Put the best cook on Cook work today\"");
        serialized.Should().NotContain("\"why\"");
        serialized.Should().Contain("\"work_type\":\"cook\"");
        serialized.Should().Contain("\"options\":[");
        serialized.Should().Contain("\"blueprint_group\":");
        serialized.Should().Contain("\"map_id\":1");
        serialized.Should().Contain("\"stuff_def_name\":\"BlocksGranite\"");
        serialized.Should().Contain("\"est_materials\":[");
        roundTripped.Should().NotBeNull();
        roundTripped!.Priority.Should().Be(Priority.High);
        roundTripped.Actions.Single().Instruction.Should().Be("Put the best cook on Cook work today");
        roundTripped.Actions.Single().WorkType.Should().Be(WorkType.Cook);
        roundTripped.Actions.Single().Skill.Should().Be("Cooking");
        AdviceOption option = roundTripped.Options.Should().ContainSingle().Subject;
        option.BlueprintGroup.Assets.Should().ContainSingle().Which.Cell.Should().Be(new MapCell(12, 34));
        option.EstimatedMaterials.Should().ContainSingle().Which.Count.Should().Be(5);
    }

    [Fact]
    public void AdviceActionKinds_SerializeWithSelfDocumentingNames()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        IReadOnlyList<AdviceAction> actions =
        [
            new AdviceAction(AdviceActionKind.MarkHarvest, "mark crops"),
            new AdviceAction(AdviceActionKind.MarkHunt, "mark animals"),
            new AdviceAction(AdviceActionKind.PlaceBlueprint, "place stove"),
            new AdviceAction(AdviceActionKind.ProductionBill, "cook meals"),
            new AdviceAction(AdviceActionKind.SetStockpileZone, "set food stockpile"),
            new AdviceAction(AdviceActionKind.Unforbid, "unforbid meals")
        ];

        string serialized = JsonSerializer.Serialize(actions, json);

        serialized.Should().Contain("\"kind\":\"mark_harvest\"");
        serialized.Should().Contain("\"kind\":\"mark_hunt\"");
        serialized.Should().Contain("\"instruction\":\"mark crops\"");
        serialized.Should().Contain("\"kind\":\"place_blueprint\"");
        serialized.Should().Contain("\"kind\":\"production_bill\"");
        serialized.Should().Contain("\"kind\":\"set_stockpile_zone\"");
        serialized.Should().Contain("\"kind\":\"unforbid\"");
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
              "priority": "high",
              "title": "Set up the food chain",
              "body": "Make storage visible, place cooking, and start growing.",
              "rationale": "reported food units need reachable stockpile visibility.",
              "actions": [
                {
                  "kind": "set_stockpile_zone",
                  "instruction": "Make the reported food units visible in a reachable stockpile.",
                  "reason": "reported food units need reachable stockpile visibility"
                }
              ],
              "guide_citations": [],
              "stamp": {
                "issued_at": "5500-04-08T16:00:00Z",
                "expires_at": "5500-04-09T16:00:00Z"
              }
            }
          ],
          "flags": [
            {
              "id": "manual_flag",
              "source_minister": "Food",
              "priority": "high",
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
        result.DroppedFlagCount.Should().Be(0);
        result.Response.StateSummary.Should().Be("Food is in crisis and needs storage visibility plus setup.");
        AdviceItem advice = result.Response.Advice.Should().ContainSingle().Subject;
        advice.Actions.Should().ContainSingle().Which.Instruction.Should().Be("Make the reported food units visible in a reachable stockpile.");
        advice.IssuedAt.Should().BeAfter(before);
        advice.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        advice.IssuedGameDate.Should().Be(FoodBriefing(0.21f).Date);
        advice.IssuedGameTick.Should().Be(300_000);
        advice.ExpiresGameTick.Should().Be(360_000);
        advice.Minister.Should().Be("Chef");
        advice.BriefingRef.Should().Be(new BriefingRef("Chef", 1, "food:1"));
        result.Response.Flags.Should().ContainSingle().Which.SourceMinister.Should().Be("Chef");
        result.Response.Flags.Should().ContainSingle().Which.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void FoodLlmResponseParser_CountsFlagObjectDroppedForMissingEnvelope()
    {
        string raw = FoodResponseWithFlags("""
        [
          {
            "building_requests": [
              {
                "request": "starter freezer",
                "reason": "incoming food needs cold storage",
                "target_class": "freezer"
              }
            ]
          }
        ]
        """);

        FoodLlmParseResult result = FoodLlmResponseParser.Parse(raw, FoodBriefing(0.21f), []);

        result.ParseMode.Should().Be("tolerant_normalization");
        result.Normalized.Should().BeTrue();
        result.DroppedFlagCount.Should().Be(1);
        result.Response.Flags.Should().BeEmpty();
    }

    [Fact]
    public void FoodLlmResponseParser_CountsFlagObjectDroppedForInvalidTargetClass()
    {
        string raw = FoodResponseWithFlags("""
        [
          {
            "id": "food:freezer_support",
            "source_minister": "Chef",
            "priority": "medium",
            "domain": "food",
            "summary": "Freezer support needed",
            "building_requests": [
              {
                "request": "starter freezer",
                "reason": "incoming food needs cold storage",
                "target_class": "cold_room"
              }
            ]
          }
        ]
        """);

        FoodLlmParseResult result = FoodLlmResponseParser.Parse(raw, FoodBriefing(0.21f), []);

        result.ParseMode.Should().Be("tolerant_normalization");
        result.Normalized.Should().BeTrue();
        result.DroppedFlagCount.Should().Be(1);
        result.Response.Flags.Should().BeEmpty();
    }

    [Fact]
    public void FoodLlmResponseParser_KeepsWellFormedFlagEnvelope()
    {
        string raw = FoodResponseWithFlags("""
        [
          {
            "id": "food:freezer_support",
            "source_minister": "Chef",
            "priority": "medium",
            "domain": "food",
            "summary": "Freezer support needed",
            "building_requests": [
              {
                "request": "starter freezer",
                "reason": "incoming food needs cold storage",
                "target_class": "freezer"
              }
            ]
          }
        ]
        """);

        FoodLlmParseResult result = FoodLlmResponseParser.Parse(raw, FoodBriefing(0.21f), []);

        result.DroppedFlagCount.Should().Be(0);
        result.Response.Flags.Should().ContainSingle().Which.BuildingRequests.Should().ContainSingle()
            .Which.TargetClass.Should().Be(BuildingClass.Freezer);
    }

    private static string FoodResponseWithFlags(string flagsJson) => $$"""
    {
      "state_summary": "Food is in crisis and needs storage visibility plus setup.",
      "advice": [
        {
          "id": "manual_food",
          "minister": "Food",
          "priority": "high",
          "title": "Set up the food chain",
          "body": "Make storage visible, place cooking, and start growing.",
          "rationale": "reported food units need reachable stockpile visibility.",
          "actions": [
            {
              "kind": "set_stockpile_zone",
              "instruction": "Make the reported food units visible in a reachable stockpile."
            }
          ],
          "guide_citations": []
        }
      ],
      "flags": {{flagsJson}},
      "notes": "food-chain-bootstrap"
    }
    """;

    private static FoodBriefing FoodBriefing(float days) => new(
        BriefingVersion: 1,
        Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
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
