using FluentAssertions;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.LLM;

namespace RimBob.Tests.Food;

public sealed class FoodPromptTests
{
    [Fact]
    public void BuildFoodUserMessage_IncludesBriefingContextAndClosedConcerns()
    {
        PromptBuilder builder = new();
        string json = builder.BuildFoodUserMessage(
            FoodRulesTests.Briefing(12f),
            new MinisterBriefingContext(null, "stabilize food", ["food"]),
            []);

        json.Should().Contain("allowed_concerns");
        json.Should().Contain("FoodSecurity");
        json.Should().Contain("minister_context");
        json.Should().Contain("stabilize food");
        json.Should().Contain("UnclassifiedFoodUnits");
        json.Should().Contain("UnclassifiedFoodItems");
        json.Should().Contain("UnknownFoodUnits");
        json.Should().Contain("ExcludedFoodUnits");
        json.Should().Contain("MissingBriefingSignals");
    }

    [Fact]
    public void BuildFoodUserMessage_IncludesComputedCropCandidatesWhenProvided()
    {
        PromptBuilder builder = new();
        string json = builder.BuildFoodUserMessage(
            FoodRulesTests.Briefing(16f),
            MinisterBriefingContext.Empty,
            [],
            [
                new FoodPromptCropCandidate(
                    CropDef: "Plant_Rice",
                    Label: "rice",
                    Tiles: 36,
                    HarvestNutrition: 0.05f,
                    GrowDays: 3f,
                    ProjectedDaysAdded: 2.25f,
                    FitsSeason: true,
                    DaysToWinterMargin: 46f,
                    ClassificationConfidence: 0.9f,
                    StorageMultiplier: 0.88f,
                    Reason: "fastest food crop with 46 days of winter margin")
            ]);

        json.Should().Contain("\"crop_candidates\"");
        json.Should().Contain("\"crop_def\":\"Plant_Rice\"");
        json.Should().Contain("\"harvest_nutrition\":0.05");
        json.Should().Contain("\"projected_days_added\":2.25");
        json.Should().Contain("\"classification_confidence\":0.9");
        json.Should().Contain("\"storage_multiplier\":0.88");
        json.Should().Contain("fastest food crop");
    }

    [Fact]
    public void FoodSystemPrompt_RequiresConcreteNearTermAdvice()
    {
        PromptBuilder builder = new();

        builder.FoodSystemPrompt.Should().Contain("near-term, actionable, currently possible advice");
        builder.FoodSystemPrompt.Should().Contain("priority: low, medium, high, or critical");
        builder.FoodSystemPrompt.Should().Contain("Keep structured leaf strings terse");
        builder.FoodSystemPrompt.Should().Contain("Titles are 3-7 words");
        builder.FoodSystemPrompt.Should().Contain("actions[].instruction is one short imperative sentence");
        builder.FoodSystemPrompt.Should().Contain("instruction is one short imperative sentence");
        builder.FoodSystemPrompt.Should().Contain("Put explanation in body and rationale");
        builder.FoodSystemPrompt.Should().Contain("kind and instruction fields");
        builder.FoodSystemPrompt.Should().NotContain("actions[].reason");
        builder.FoodSystemPrompt.Should().NotContain("action reason");
        builder.FoodSystemPrompt.Should().Contain("mark_hunt");
        builder.FoodSystemPrompt.Should().Contain("unforbid");
        builder.FoodSystemPrompt.Should().Contain("Flags must be full AgentFlag objects");
        builder.FoodSystemPrompt.Should().Contain("severity (low, medium, high, or critical)");
        builder.FoodSystemPrompt.Should().Contain("domain \"food\"");
        builder.FoodSystemPrompt.Should().Contain("A flag missing this envelope or using an unknown enum token is dropped");
        builder.FoodSystemPrompt.Should().Contain("building_requests");
        builder.FoodSystemPrompt.Should().Contain("labor_requests");
        builder.FoodSystemPrompt.Should().Contain("item_requests");
        builder.FoodSystemPrompt.Should().Contain("freezer, wall, door");
        builder.FoodSystemPrompt.Should().Contain("production_bench");
        builder.FoodSystemPrompt.Should().Contain("plant_cut");
        builder.FoodSystemPrompt.Should().NotContain("kind, what, why");
        builder.FoodSystemPrompt.Should().Contain("Do not ask for generic \"labor capacity\"");
        builder.FoodSystemPrompt.Should().Contain("Trade-for-food is not day-one local advice");
        builder.FoodSystemPrompt.Should().Contain("When crop_candidates is present");
        builder.FoodSystemPrompt.Should().Contain("computed crop math");
        builder.FoodSystemPrompt.Should().Contain("food units need meal/raw-food classification in a reachable stockpile");
        builder.FoodSystemPrompt.Should().Contain("If UnforbidTargets names forbidden meals");
        builder.FoodSystemPrompt.Should().Contain("forbidden food units outside the current food buffer");
        builder.FoodSystemPrompt.Should().Contain("Do not infer they are edible");
        builder.FoodSystemPrompt.Should().Contain("MissingBriefingSignals and UnimplementedBriefingSignals");
        builder.FoodSystemPrompt.Should().Contain("notes field is a terse trace label");
        builder.FoodSystemPrompt.Should().Contain("Do not start notes with \"Briefing indicates\"");
        builder.FoodSystemPrompt.Should().Contain("do not use speculative prose");
        builder.FoodSystemPrompt.Should().Contain("Do not invent coordinates");
    }
}
