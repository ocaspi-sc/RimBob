using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.State;

namespace RimBob.Tests.State;

public sealed class ColonyStateFreezeTests
{
    [Fact]
    public void Capture_ReferenceCopiesAggregateValuesAndIgnoresLaterLiveUpdates()
    {
        ColonyState live = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live,
            LastLiveRefreshAt = DateTimeOffset.UnixEpoch
        };
        live.Map.Update(new MapInfoSnapshot(7, "(250,1,250)"));
        live.Buildings.Update(new BuildingRegistry([
            new BuildingRecord("cooler-1", "Cooler", 1f, true, true)
        ]));

        ColonyState frozen = ColonyStateFreeze.Capture(live);
        live.Buildings.Update(new BuildingRegistry([]));

        frozen.Should().NotBeSameAs(live);
        frozen.LastRefreshSource.Should().Be(ColonyStateOrigin.Live);
        frozen.LastLiveRefreshAt.Should().Be(DateTimeOffset.UnixEpoch);
        frozen.Map.Value.Id.Should().Be(7);
        frozen.Buildings.Value.Buildings.Should().ContainSingle()
            .Which.Id.Should().Be("cooler-1");
        live.Buildings.Value.Buildings.Should().BeEmpty();
    }
}
