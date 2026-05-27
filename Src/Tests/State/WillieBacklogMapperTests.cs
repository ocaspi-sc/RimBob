using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;
using RimBob.State;

namespace RimBob.Tests.State;

public sealed class WillieBacklogMapperTests
{
    [Fact]
    public void FromConstructionBacklog_MapsForkDtoIntoAggregate()
    {
        ConstructionBacklogGroupDto dto = new()
        {
            Kind = "Blueprint",
            DefName = "Cooler",
            StuffDefName = "Steel",
            Allowed = true,
            Count = 2,
            ThingIds = [101, 102],
            SampleCells = [new MapCellDto(10, 20)],
            TotalWorkLeft = 450f,
            Cost = [new ConstructionMaterialCountDto { DefName = "Steel", Count = 90 }],
            MaterialsAvailable =
            [
                new ConstructionMaterialAvailabilityDto
                {
                    DefName = "Steel",
                    Required = 90,
                    Available = 60,
                    Missing = 30
                }
            ],
            MaterialsMissing =
            [
                new ConstructionMaterialAvailabilityDto
                {
                    DefName = "ComponentIndustrial",
                    Required = 2,
                    Available = 0,
                    Missing = 2
                }
            ],
            BlockedCount = 1,
            DisallowedCount = 0
        };

        WillieConstructionBacklog backlog = WillieBacklogMapper.FromConstructionBacklog([dto]);

        backlog.SourceAvailable.Should().BeTrue();
        WillieBacklogGroup group = backlog.Groups.Should().ContainSingle().Subject;
        group.Kind.Should().Be("Blueprint");
        group.DefName.Should().Be("Cooler");
        group.ThingIds.Should().Equal("101", "102");
        group.SampleCells.Should().ContainSingle().Which.Should().Be(new MapPosition(10, 0, 20));
        group.TotalWorkLeft.Should().Be(450f);
        group.Cost.Should().ContainSingle().Which.Should().Be(new MaterialCount("Steel", 90));
        MaterialCount missing = group.MaterialsMissing.Should().ContainSingle().Subject;
        missing.DefName.Should().Be("ComponentIndustrial");
        missing.Count.Should().Be(2);
        missing.Required.Should().Be(2);
        missing.Available.Should().Be(0);
        missing.Missing.Should().Be(2);
    }
}
