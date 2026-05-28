using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Ministers;
using RimBob.Ministers.Willie;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Willie;

public sealed class MinisterOfWillieTests
{
    [Fact]
    public async Task RuleDecision_PublishesWillieAdviceSnapshot()
    {
        Harness harness = new();
        harness.SetStableState();
        harness.Colony.Power.Update(new PowerNetwork(ProductionW: 100, ConsumptionW: 500, StoredWd: 0, CapacityWd: 0));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Willie");
        advice.Concern.Should().Be("power_stability");
        harness.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Willie")
            .WhoseValue.Should().Contain("Power:");
    }

    [Fact]
    public async Task InboundFreezerFlag_PublishesThermalControlAdvice()
    {
        Harness harness = new();
        harness.SetStableState();
        harness.Flags.Publish(new AgentFlag(
            Id: "food:freezer_missing",
            SourceMinister: "Chef",
            Severity: FlagSeverity.Medium,
            Domain: "food",
            Summary: "Food storage needs freezer support",
            BuildingRequests:
            [
                new BuildingRequest(
                    Request: "starter freezer near kitchen",
                    Reason: "Chef needs cold storage",
                    TargetClass: BuildingClass.Freezer,
                    TargetDef: "Cooler",
                    RoomClass: RoomClass.Freezer,
                    Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
                    Priority: AdvicePriority.Medium,
                    RequestedFrom: "Willie")
            ]));

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualTrigger, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Willie");
        advice.Concern.Should().Be("thermal_control");
        advice.Actions.Should().ContainSingle().Which.Apply.Should().BeNull();
    }

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public AdviceBus Bus { get; } = new();
        public FlagChannel Flags { get; } = new();
        public MinisterOfWillie Minister { get; }

        public Harness()
        {
            BriefingCache cache = new(Colony, new TestLogger<BriefingCache>());
            Minister = new(
                cache,
                new Rules(),
                new MinisterOutputStore(),
                Bus,
                Flags,
                NullLogger<MinisterOfWillie>.Instance);
        }

        public void SetStableState()
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = DateTimeOffset.UtcNow;
            Colony.Map.Update(new MapInfoSnapshot(7, "(250,1,250)"));
            Colony.Economy.Update(new EconomyLedger(300_000, 0f, "", "", false, "5th of Aprimay, 5500, 14h"));
            Colony.Colonists.Update(new ColonistRegistry([
                new ColonistRecord("p1", "Alice", 28, "Female", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p2", "Bob", 30, "Male", 1f, 0.7f, 1f, false, false, null, null, [], []),
                new ColonistRecord("p3", "Cora", 32, "Female", 1f, 0.7f, 1f, false, false, null, null, [], [])
            ]));
            Colony.Rooms.Update(new RoomRegistry([
                Room("kitchen-room", "Kitchen"),
                Room("hospital-room", "Hospital"),
                Room("storage-room", "Storage")
            ]));
            Colony.Stockpiles.Update(new StockpileLedger(
                [new StockpileZone("stockpile-1", "stockpile", "Main storage", 40)],
                new Dictionary<string, int>()));
            Colony.Buildings.Update(new BuildingRegistry([
                new BuildingRecord("cooler-1", "Cooler", 1f, true, true),
                new BuildingRecord("generator-1", "WoodFiredGenerator", 1f, true, true),
                new BuildingRecord("battery-1", "Battery", 1f, true, true)
            ]));
            Colony.Power.Update(new PowerNetwork(ProductionW: 900, ConsumptionW: 650, StoredWd: 800, CapacityWd: 1000));
        }

        private static RoomRecord Room(string id, string role) =>
            new(
                Id: id,
                RoleLabel: role,
                Temperature: 21,
                CellsCount: 25,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: [],
                Impressiveness: null,
                Beauty: null,
                Cleanliness: null,
                Space: null,
                Wealth: null);
    }
}
