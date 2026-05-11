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
    }
}
