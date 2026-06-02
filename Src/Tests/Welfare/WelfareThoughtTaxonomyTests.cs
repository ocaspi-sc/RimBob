using FluentAssertions;
using RimBob.Core.Briefings;

namespace RimBob.Tests.Welfare;

public sealed class WelfareThoughtTaxonomyTests
{
    [Theory]
    [InlineData("SleptOutside", null, ThoughtCategory.ShelterSleep, "Willie")]
    [InlineData("Bored", "bored", ThoughtCategory.Recreation, "Willie")]
    [InlineData("AteWithoutTable", "ate without table", ThoughtCategory.ComfortBeauty, "Willie")]
    [InlineData("Hungry", "hungry", ThoughtCategory.Hunger, "Chef")]
    [InlineData("SleptInCold", "slept in the cold", ThoughtCategory.Temperature, "Willie")]
    [InlineData("Insulted", "insulted", ThoughtCategory.Social, null)]
    [InlineData("Pain", "pain", ThoughtCategory.Health, "Medical")]
    [InlineData("IdeologyDisrespected", "ideology disrespected", ThoughtCategory.Ideology, null)]
    public void Classify_MapsKnownThoughtsAndOwners(
        string defName,
        string? label,
        ThoughtCategory category,
        string? owner)
    {
        WelfareThoughtTaxonomy.Classify(defName, label).Should().Be(category);
        WelfareThoughtTaxonomy.SuggestedOwner(category).Should().Be(owner);
    }

    [Fact]
    public void Classify_UnknownThoughtsFallBackToOther()
    {
        WelfareThoughtTaxonomy.Classify("ModdedOddThought", "some strange moodlet")
            .Should().Be(ThoughtCategory.Other);
    }
}
