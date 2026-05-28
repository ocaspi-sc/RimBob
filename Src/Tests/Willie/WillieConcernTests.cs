using System.Text.Json;
using FluentAssertions;
using RimBob.Core.Advice;

namespace RimBob.Tests.Willie;

public sealed class WillieConcernTests
{
    [Fact]
    public void WillieConcern_RoundTripsAsSnakeCase()
    {
        string json = JsonSerializer.Serialize(WillieConcern.PowerStability);

        json.Should().Be("\"power_stability\"");
        JsonSerializer.Deserialize<WillieConcern>(json).Should().Be(WillieConcern.PowerStability);
    }
}
