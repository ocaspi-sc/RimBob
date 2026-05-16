using FluentAssertions;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.LLM;

namespace RimAI.Tests.Food;

public sealed class FoodPromptTests
{
    [Fact]
    public void BuildFoodUserMessage_IncludesBriefingContextAndClosedAdviceTypes()
    {
        PromptBuilder builder = new();
        string json = builder.BuildFoodUserMessage(
            FoodRulesTests.Briefing(12f),
            new MinisterBriefingContext(null, "stabilize food", ["food"]),
            []);

        json.Should().Contain("allowed_advice_types");
        json.Should().Contain("FoodSecurity");
        json.Should().Contain("minister_context");
        json.Should().Contain("stabilize food");
        json.Should().Contain("UnclassifiedFoodUnits");
        json.Should().Contain("UnclassifiedFoodItems");
        json.Should().Contain("MissingBriefingSignals");
    }

    [Fact]
    public void FoodSystemPrompt_RequiresConcreteNearTermAdvice()
    {
        PromptBuilder builder = new();

        builder.FoodSystemPrompt.Should().Contain("\"state_summary\"");
        builder.FoodSystemPrompt.Should().Contain("Always emit state_summary before advice");
        builder.FoodSystemPrompt.Should().Contain("high-level current-state bullet list");
        builder.FoodSystemPrompt.Should().Contain("near-term, actionable, currently possible advice");
        builder.FoodSystemPrompt.Should().Contain("priority: low, medium, high, or critical");
        builder.FoodSystemPrompt.Should().Contain("Keep structured leaf strings terse");
        builder.FoodSystemPrompt.Should().Contain("Titles are 3-7 words");
        builder.FoodSystemPrompt.Should().Contain("steps[].instruction is one short imperative sentence");
        builder.FoodSystemPrompt.Should().Contain("steps[].reason is one short cause");
        builder.FoodSystemPrompt.Should().Contain("instruction is one short imperative sentence");
        builder.FoodSystemPrompt.Should().Contain("Put explanation in body and rationale");
        builder.FoodSystemPrompt.Should().Contain("kind and instruction fields");
        builder.FoodSystemPrompt.Should().Contain("mark_hunt");
        builder.FoodSystemPrompt.Should().Contain("unforbid");
        builder.FoodSystemPrompt.Should().Contain("Flags may carry Resource requests");
        builder.FoodSystemPrompt.Should().NotContain("kind, what, why");
        builder.FoodSystemPrompt.Should().Contain("Do not ask for generic \"labor capacity\"");
        builder.FoodSystemPrompt.Should().Contain("Trade-for-food is not day-one local advice");
        builder.FoodSystemPrompt.Should().Contain("unclassified food units need reachable stockpile visibility");
        builder.FoodSystemPrompt.Should().Contain("UnclassifiedFoodItems names forbidden meals");
        builder.FoodSystemPrompt.Should().Contain("Do not infer they are edible");
        builder.FoodSystemPrompt.Should().Contain("MissingBriefingSignals and UnimplementedBriefingSignals");
        builder.FoodSystemPrompt.Should().Contain("notes field is a terse trace label");
        builder.FoodSystemPrompt.Should().Contain("Do not start notes with \"Briefing indicates\"");
        builder.FoodSystemPrompt.Should().Contain("do not use speculative prose");
        builder.FoodSystemPrompt.Should().Contain("Do not invent coordinates");
    }
}
