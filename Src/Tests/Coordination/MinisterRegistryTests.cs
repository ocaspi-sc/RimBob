using FluentAssertions;
using RimBob.Coordination;

namespace RimBob.Tests.Coordination;

public sealed class MinisterRegistryTests
{
    [Fact]
    public void Scopes_PreserveDashboardOrderAndReadiness()
    {
        MinisterRegistry sut = new();

        sut.Scopes.Select(scope => scope.Key).Should().Equal(
            "system",
            "mayor",
            "food",
            "willie",
            "defense",
            "welfare",
            "medical",
            "research",
            "industry",
            "economy",
            "chief_of_staff");

        sut.Find("mayor")!.Ready.Should().BeTrue();
        sut.Find("welfare")!.Ready.Should().BeTrue();
        sut.Find("willie")!.Ready.Should().BeTrue();
    }

    [Fact]
    public void CabinetMinisters_RunFoodBeforeMayor()
    {
        MinisterRegistry sut = new();

        sut.CabinetMinisters.Select(scope => scope.Key).Should().Equal("food", "welfare", "willie", "mayor");
    }

    [Theory]
    [InlineData("food", "food")]
    [InlineData("Food", "food")]
    [InlineData("Chef", "food")]
    [InlineData("Willie", "willie")]
    [InlineData("Chief of Staff", "chief_of_staff")]
    [InlineData("chief-of-staff", "chief_of_staff")]
    public void FindMinister_ResolvesKeysAndLabels(string input, string expectedKey)
    {
        MinisterRegistry sut = new();

        MinisterDescriptor? descriptor = sut.FindMinister(input);

        descriptor.Should().NotBeNull();
        descriptor!.Key.Should().Be(expectedKey);
    }

    [Fact]
    public void ManualTriggerCapability_IsLimitedToWiredMinisters()
    {
        MinisterRegistry sut = new();

        sut.FindMinister("mayor")!.CanManualTrigger.Should().BeTrue();
        sut.FindMinister("food")!.CanManualTrigger.Should().BeTrue();
        sut.FindMinister("welfare")!.CanManualTrigger.Should().BeTrue();
        sut.FindMinister("willie")!.CanManualTrigger.Should().BeTrue();
        sut.FindMinister("mayor")!.CanRunLlm.Should().BeTrue();
        sut.FindMinister("mayor")!.CanRunRules.Should().BeFalse();
        sut.FindMinister("food")!.CanRunLlm.Should().BeTrue();
        sut.FindMinister("food")!.CanRunRules.Should().BeTrue();
        sut.FindMinister("welfare")!.CanRunLlm.Should().BeTrue();
        sut.FindMinister("welfare")!.CanRunRules.Should().BeTrue();
        sut.FindMinister("welfare")!.HasPrompt.Should().BeTrue();
        sut.FindMinister("welfare")!.HasRag.Should().BeTrue();
        sut.FindMinister("welfare")!.HasRawLlmOutput.Should().BeTrue();
        sut.FindMinister("welfare")!.HasManualLlmOutput.Should().BeTrue();
        sut.FindMinister("welfare")!.EnabledViews.Should().Equal("prompt", "briefing", "rag", "rules", "raw_llm", "advice");
        sut.FindMinister("willie")!.CanRunLlm.Should().BeFalse();
        sut.FindMinister("willie")!.CanRunRules.Should().BeTrue();
        sut.FindMinister("willie")!.EnabledViews.Should().Equal("briefing", "solver", "requests", "rules", "advice");
    }
}
