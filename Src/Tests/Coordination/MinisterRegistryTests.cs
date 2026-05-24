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
            "construction",
            "defense",
            "welfare",
            "medical",
            "research",
            "industry",
            "economy",
            "chief_of_staff");

        sut.Find("mayor")!.Ready.Should().BeTrue();
        sut.Find("construction")!.Ready.Should().BeFalse();
    }

    [Fact]
    public void CabinetMinisters_RunFoodBeforeMayor()
    {
        MinisterRegistry sut = new();

        sut.CabinetMinisters.Select(scope => scope.Key).Should().Equal("food", "mayor");
    }

    [Theory]
    [InlineData("food", "food")]
    [InlineData("Food", "food")]
    [InlineData("Chef", "food")]
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
        sut.FindMinister("construction")!.CanManualTrigger.Should().BeFalse();
    }
}
