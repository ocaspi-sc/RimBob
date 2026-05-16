using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;
using RimBob.LLM;

namespace RimBob.Tests.LLM;

public sealed class AdviceTextStyleWarningsTests
{
    [Fact]
    public void ForFood_WarnsWhenStructuredLeafStringsAreWordy()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FoodLlmResponse response = new(
            StateSummary: "Food is strained.",
            Advice:
            [
                new AdviceItem(
                    Id: "wordy_food",
                    Minister: "Food",
                    AdviceType: "food_security",
                    Priority: AdvicePriority.High,
                    Title: "Confirm the reachable indoor stockpile visibility before making meal decisions",
                    Body: "Body can carry context.",
                    Rationale: "Rationale can carry explanation.",
                    Steps:
                    [
                        new AdviceStep(
                            AdviceStepKind.SetStockpileZone,
                            "Set one reachable indoor stockpile to accept meals. Verify the stored food category.",
                            Reason: "Food classification is missing and the buffer is below one day for the whole colony")
                    ],
                    GuideCitationIds: [],
                    IssuedAt: now,
                    ExpiresAt: now.AddHours(4))
            ],
            Flags:
            [
                new AgentFlag(
                    "wordy_flag",
                    "Food",
                    FlagSeverity.High,
                    "food",
                    "Food buffer is below one day while storage visibility and emergency growing coverage both remain blocked")
            ]);

        IReadOnlyList<string> warnings = AdviceTextStyleWarnings.ForFood(response);

        warnings.Should().Contain(warning => warning.Contains("wordy_food.title"));
        warnings.Should().Contain(warning => warning.Contains("steps[0].instruction"));
        warnings.Should().Contain(warning => warning.Contains("steps[0].reason"));
        warnings.Should().Contain(warning => warning.Contains("multiple sentences"));
        warnings.Should().Contain(warning => warning.Contains("wordy_flag.summary"));
    }

    [Fact]
    public void ForFood_AllowsCompactStructuredLeafStrings()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FoodLlmResponse response = new(
            StateSummary: "Food is strained.",
            Advice:
            [
                new AdviceItem(
                    Id: "compact_food",
                    Minister: "Food",
                    AdviceType: "food_security",
                    Priority: AdvicePriority.High,
                    Title: "Expose food stockpile",
                    Body: "Body can carry context.",
                    Rationale: "Rationale can carry explanation.",
                    Steps:
                    [
                        new AdviceStep(
                            AdviceStepKind.SetStockpileZone,
                            "Set one stockpile to accept food.",
                            Reason: "Food classification is missing.")
                    ],
                    GuideCitationIds: [],
                    IssuedAt: now,
                    ExpiresAt: now.AddHours(4))
            ],
            Flags:
            [
                new AgentFlag(
                    "compact_flag",
                    "Food",
                    FlagSeverity.High,
                    "food",
                    "Food buffer below one day.")
            ]);

        AdviceTextStyleWarnings.ForFood(response).Should().BeEmpty();
    }
}
