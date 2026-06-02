using System.Text.Json;
using FluentAssertions;
using RimBob.Core.Advice;

namespace RimBob.Tests.Welfare;

public sealed class WelfareConcernTests
{
    [Fact]
    public void WelfareConcern_RoundTripsAsSnakeCase()
    {
        string json = JsonSerializer.Serialize(WelfareConcern.ShelterFloor);

        json.Should().Be("\"shelter_floor\"");
        JsonSerializer.Deserialize<WelfareConcern>(json).Should().Be(WelfareConcern.ShelterFloor);
    }
}
