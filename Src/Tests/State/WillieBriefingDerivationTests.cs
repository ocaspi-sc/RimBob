using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.State.Derivations;

namespace RimBob.Tests.State;

public sealed class WillieBriefingDerivationTests
{
    [Fact]
    public void Compute_SurfacesBacklogOnMaterialAndStalledSummaries()
    {
        ColonyState state = StateWithOneBacklogGroup();

        WillieBriefing briefing = WillieBriefingDerivation.Compute(state);

        briefing.MaterialBottleneck.BacklogGroups.Should().ContainSingle()
            .Which.DefName.Should().Be("Cooler");
        briefing.MaterialBottleneck.MissingMaterials.Should().ContainSingle()
            .Which.Should().Be(new MaterialCount("ComponentIndustrial", 2));
        briefing.MaterialBottleneck.BlockedCount.Should().Be(1);
        briefing.StalledBuilds.BacklogGroups.Should().ContainSingle()
            .Which.DefName.Should().Be("Cooler");
        briefing.StalledBuilds.PendingBuildCount.Should().Be(2);
        briefing.StalledBuilds.TotalWorkLeft.Should().Be(450f);
        briefing.DataCoverage.HasConstructionBacklog.Should().BeTrue();
    }

    private static ColonyState StateWithOneBacklogGroup()
    {
        ColonyState state = new()
        {
            LastRefreshSource = ColonyStateOrigin.Live,
            LastLiveRefreshAt = DateTimeOffset.UtcNow
        };

        state.Map.Update(new MapInfoSnapshot(7, "(250,1,250)"));
        state.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, "5th of Aprimay, 5500, 14h"));
        state.Colonists.Update(new ColonistRegistry([
            new ColonistRecord(
                Id: "p1",
                Name: "Alice",
                Age: 28,
                Gender: "Female",
                Health: 1f,
                Mood: 0.7f,
                Hunger: 1f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [],
                Traits: [])
        ]));
        state.WillieBacklog.Update(new WillieConstructionBacklog([
            new WillieBacklogGroup(
                Kind: "Blueprint",
                DefName: "Cooler",
                StuffDefName: "Steel",
                Allowed: true,
                Count: 2,
                ThingIds: ["101", "102"],
                SampleCells: [new MapPosition(10, 0, 20)],
                TotalWorkLeft: 450f,
                Cost: [new MaterialCount("Steel", 90)],
                MaterialsAvailable: [new MaterialCount("Steel", 60)],
                MaterialsMissing: [new MaterialCount("ComponentIndustrial", 2)],
                BlockedCount: 1,
                DisallowedCount: 0)
        ])
        {
            SourceAvailable = true
        });

        return state;
    }
}
