using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.LLM;

namespace RimAI.Tests.LLM;

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
    public void ResourceRequestNormalizer_RepairsLegacyAliasesAndWorkType()
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

        IReadOnlyList<ResourceRequest> requests = ResourceRequestNormalizer.Normalize(
            root,
            AdvicePriority.High,
            Context(),
            Json);

        ResourceRequest request = requests.Should().ContainSingle().Subject;
        request.Kind.Should().Be(ResourceRequestKind.Labor);
        request.WorkType.Should().Be(WorkType.Cook);
        request.Skill.Should().Be("Cooking");
        request.Priority.Should().Be(AdvicePriority.High);
        request.RequestedFrom.Should().Be("Labor");
    }

    [Fact]
    public void AdviceStepNormalizer_ConvertsLegacyActionsAndTextFallback()
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

        IReadOnlyList<AdviceStep> steps = AdviceStepNormalizer.NormalizeOrConvertLegacy(
            stepsNode: null,
            resourceRequestsNode: null,
            suggestedActionsNode: root,
            priority: AdvicePriority.Medium,
            context: Context(),
            json: Json);

        steps.Should().HaveCount(2);
        steps[0].Kind.Should().Be(AdviceStepKind.ProductionBill);
        steps[0].Instruction.Should().Be("cook simple meals");
        steps[1].Kind.Should().Be(AdviceStepKind.Note);
        steps[1].Instruction.Should().Be("check whether berries are reachable");
    }

    [Fact]
    public void AdviceStepNormalizer_StripsModelSuppliedApplyMetadata()
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

        IReadOnlyList<AdviceStep> steps = AdviceStepNormalizer.Normalize(root, Json);

        AdviceStep step = steps.Should().ContainSingle().Subject;
        step.Kind.Should().Be(AdviceStepKind.MarkHarvest);
        step.Apply.Should().BeNull();
    }

    private static LlmAdviceNormalizationContext Context() => new(
        Minister: "Food",
        Domain: "food",
        BriefingVersion: 1,
        GameTick: 300_000,
        Date: new DateStamp("5th of Aprimay, 5500, 14h", 5500, "Aprimay", 5, 14),
        DefaultAdviceType: "food_security",
        DefaultRationale: "Food LLM escalation selected this recommendation.",
        GuideContext: []);
}
