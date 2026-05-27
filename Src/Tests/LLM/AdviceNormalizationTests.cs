using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.LLM;

namespace RimBob.Tests.LLM;

public sealed class AdviceNormalizationTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void AdviceJsonCompatibility_MapsLegacyPriorityScore()
    {
        JsonNode? score = JsonNode.Parse("8");

        AdvicePriority priority = AdviceJsonCompatibility.ParseAdvicePriority(
            raw: null,
            legacyPriorityScore: score,
            fallback: AdvicePriority.Low);

        priority.Should().Be(AdvicePriority.High);
    }

    [Fact]
    public void WorkTypeInference_MapsAliasesAndDefaultSkills()
    {
        WorkTypeInference.Parse("plant cut").Should().Be(WorkType.PlantCut);
        WorkTypeInference.Infer("forage berries near storage").Should().Be(WorkType.PlantCut);
        WorkTypeInference.DefaultSkill(WorkType.Cook).Should().Be("Cooking");
    }

    [Fact]
    public void ResourceRequestNormalizer_MapsLegacyRequestsIntoTypedArrays()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "labor",
            "what": "cook simple meals",
            "why": "meal stock is low"
          }
        ]
        """);

        NormalizedFlagRequests requests = ResourceRequestNormalizer.NormalizeLegacyRequests(
            root,
            AdvicePriority.High,
            Context(),
            Json);

        LaborRequest request = requests.LaborRequests.Should().ContainSingle().Subject;
        request.WorkType.Should().Be(WorkType.Cook);
        request.Skill.Should().Be("Cooking");
        request.Priority.Should().Be(AdvicePriority.High);
        request.RequestedFrom.Should().Be("Labor");
    }

    [Fact]
    public void ResourceRequestNormalizer_DefaultsLegacyBuildingRequestsToWillie()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "building",
            "what": "cooler-backed freezer",
            "why": "food will spoil"
          }
        ]
        """);

        NormalizedFlagRequests requests = ResourceRequestNormalizer.NormalizeLegacyRequests(
            root,
            AdvicePriority.High,
            Context(),
            Json);

        BuildingRequest request = requests.BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Freezer);
        request.Priority.Should().Be(AdvicePriority.High);
        request.RequestedFrom.Should().Be("Willie");
    }

    [Fact]
    public void AdviceActionNormalizer_ConvertsLegacyActionsAndTextFallback()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "production_bill",
            "what": "cook simple meals"
          },
          "check whether berries are reachable"
        ]
        """);

        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.NormalizeOrConvertLegacy(
            actionsNode: null,
            legacyStepsNode: null,
            resourceRequestsNode: null,
            suggestedActionsNode: root,
            priority: AdvicePriority.Medium,
            context: Context(),
            json: Json);

        actions.Should().HaveCount(2);
        actions[0].Kind.Should().Be(AdviceActionKind.ProductionBill);
        actions[0].Instruction.Should().Be("cook simple meals");
        actions[1].Kind.Should().Be(AdviceActionKind.Note);
        actions[1].Instruction.Should().Be("check whether berries are reachable");
    }

    [Fact]
    public void AdviceActionNormalizer_AcceptsLegacyStepsField()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "mark_harvest",
            "instruction": "mark mature rice"
          }
        ]
        """);

        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.NormalizeOrConvertLegacy(
            actionsNode: null,
            legacyStepsNode: root,
            resourceRequestsNode: null,
            suggestedActionsNode: null,
            priority: AdvicePriority.Medium,
            context: Context(),
            json: Json);

        AdviceAction action = actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.MarkHarvest);
        action.Instruction.Should().Be("mark mature rice");
    }

    [Fact]
    public void AdviceActionNormalizer_StripsModelSuppliedApplyMetadata()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "mark_harvest",
            "instruction": "mark these berries",
            "apply": {
              "kind": "mark_harvest_area",
              "label": "unsafe model apply",
              "target_summary": "model supplied target",
              "map_id": 1,
              "target_count": 1,
              "rect": { "x1": 1, "z1": 1, "x2": 2, "z2": 2 },
              "target_ids": ["plant-1"]
            }
          }
        ]
        """);

        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.Normalize(root, Json);

        AdviceAction action = actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.MarkHarvest);
        action.Apply.Should().BeNull();
    }

    [Fact]
    public void AdviceActionNormalizer_StripsModelSuppliedProductionBillApplyMetadata()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "production_bill",
            "instruction": "set simple meal bill",
            "apply": {
              "kind": "upsert_production_bill",
              "label": "unsafe model apply",
              "target_summary": "model supplied bill",
              "map_id": 1,
              "target_count": 999,
              "workbench_building_id": "123",
              "recipe_selector_key": "simple_meal",
              "repeat_mode": "TargetCount"
            }
          }
        ]
        """);

        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.Normalize(root, Json);

        AdviceAction action = actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.ProductionBill);
        action.Apply.Should().BeNull();
    }

    [Fact]
    public void AdviceActionNormalizer_IgnoresLegacyActionReasonAndIconKeys()
    {
        JsonNode? root = JsonNode.Parse("""
        [
          {
            "kind": "mark_harvest",
            "instruction": "mark mature rice",
            "reason": "legacy action-level explanation",
            "icon": { "kind": "item", "id": "Plant_Rice" }
          }
        ]
        """);

        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.Normalize(root, Json);

        AdviceAction action = actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.MarkHarvest);
        action.Instruction.Should().Be("mark mature rice");
        JsonSerializer.Serialize(action, Json).Should().NotContain("\"reason\"");
        JsonSerializer.Serialize(action, Json).Should().NotContain("\"icon\"");
    }

    [Fact]
    public void AdviceResponseNormalizer_ToleratesLegacyActionReasonAndIconKeys()
    {
        JsonNode root = JsonNode.Parse("""
        {
          "advice": [
            {
              "concern": "HarvestNow",
              "priority": "medium",
              "title": "Harvest rice",
              "body": "Mature rice is ready.",
              "rationale": "The crop is ready before the buffer drops.",
              "actions": [
                {
                  "kind": "mark_harvest",
                  "instruction": "mark mature rice",
                  "reason": "legacy action-level explanation",
                  "icon": { "kind": "item", "id": "Plant_Rice" }
                }
              ]
            }
          ],
          "flags": []
        }
        """)!;

        NormalizedAdviceResponse response = AdviceResponseNormalizer.Normalize(root, Context(), Json);

        AdviceAction action = response.Advice.Should().ContainSingle().Subject.Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.MarkHarvest);
        action.Instruction.Should().Be("mark mature rice");
        string serialized = JsonSerializer.Serialize(action, Json);
        serialized.Should().NotContain("\"reason\"");
        serialized.Should().NotContain("\"icon\"");
    }

    private static LlmAdviceNormalizationContext Context() => new(
        Minister: "Chef",
        Domain: "food",
        BriefingVersion: 1,
        GameTick: 300_000,
        Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
        DefaultConcern: "food_security",
        DefaultRationale: "Chef LLM escalation selected this recommendation.",
        GuideContext: []);
}
