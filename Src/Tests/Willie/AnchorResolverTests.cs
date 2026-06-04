using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class AnchorResolverTests
{
    [Fact]
    public void ResolveNear_UsesCentroidFallbackAndStableOrdering()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("kitchen-b", RoomClass.Kitchen, 40, new MapPosition(30, 0, 30)),
                Anchor("bedroom", RoomClass.Bedroom, 12, new MapPosition(5, 0, 5)),
                Anchor("kitchen-a", RoomClass.Kitchen, 10, new MapPosition(10, 0, 10))
            ])
        };
        PlacementSpec spec = SpecWithNear("kitchen");

        IReadOnlyList<ResolvedAnchor> resolved = AnchorResolver.ResolveNear(spec, briefing);

        resolved.Select(anchor => anchor.Anchor.RoomId).Should().Equal("kitchen-a", "kitchen-b");
        resolved[0].TargetCell.Should().Be(new MapPosition(10, 0, 10));
        resolved[0].MatchReason.Should().Be(AnchorMatchReason.CentroidFallback);
    }

    [Fact]
    public void ResolveNear_UsesEntryCellsBeforeCentroid()
    {
        WillieRoomAnchor kitchen = Anchor("kitchen", RoomClass.Kitchen, 20, new MapPosition(99, 0, 99)) with
        {
            EntryCells = [new MapPosition(4, 0, 5), new MapPosition(3, 0, 5)]
        };
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([kitchen])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveNear(SpecWithNear("kitchen"), briefing)
            .Should().ContainSingle().Subject;

        resolved.TargetCell.Should().Be(new MapPosition(3, 0, 5));
        resolved.MatchReason.Should().Be(AnchorMatchReason.EntryCells);
    }

    [Fact]
    public void ResolveNear_FiltersRepeatedRoomIdByFunctionClass()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("multi-room", RoomClass.Barracks, 64, new MapPosition(20, 0, 20)),
                Anchor("multi-room", RoomClass.Kitchen, 64, new MapPosition(12, 0, 11))
            ])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveNear(SpecWithNear("kitchen"), briefing)
            .Should().ContainSingle().Subject;

        resolved.Anchor.RoomId.Should().Be("multi-room");
        resolved.Anchor.Class.Should().Be(RoomClass.Kitchen);
        resolved.TargetCell.Should().Be(new MapPosition(12, 0, 11));
    }

    [Fact]
    public void ResolveNear_SkipsAnchorWithoutTargetCell()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([Anchor("kitchen", RoomClass.Kitchen, 20, null)])
        };

        AnchorResolver.ResolveNear(SpecWithNear("kitchen"), briefing).Should().BeEmpty();
    }

    [Fact]
    public void ResolveNear_UsesRoomClassAliases()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("hospital", RoomClass.Hospital, 24, new MapPosition(11, 0, 12))
            ])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveNear(SpecWithNear("medbay"), briefing)
            .Should().ContainSingle().Subject;

        resolved.Anchor.Class.Should().Be(RoomClass.Hospital);
        resolved.TargetCell.Should().Be(new MapPosition(11, 0, 12));
    }

    [Theory]
    [InlineData("Kitchen", RoomClass.Kitchen)]
    [InlineData("hospital", RoomClass.Hospital)]
    [InlineData("storage", RoomClass.Storage)]
    [InlineData("research", RoomClass.Research)]
    public void ResolveNear_UsesUnaliasedRoomClassNames(string target, RoomClass roomClass)
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("target-room", roomClass, 24, new MapPosition(11, 0, 12))
            ])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveNear(SpecWithNear(target), briefing)
            .Should().ContainSingle().Subject;

        resolved.Anchor.Class.Should().Be(roomClass);
        resolved.TargetCell.Should().Be(new MapPosition(11, 0, 12));
        resolved.MatchReason.Should().Be(AnchorMatchReason.CentroidFallback);
    }

    [Fact]
    public void ResolveBuildableRegion_UsesCentroid()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("area:0", RoomClass.BuildableRegion, 25, new MapPosition(12, 0, 22))
            ])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveBuildableRegion(briefing)
            .Should().ContainSingle().Subject;

        resolved.Anchor.Class.Should().Be(RoomClass.BuildableRegion);
        resolved.TargetCell.Should().Be(new MapPosition(12, 0, 22));
        resolved.MatchReason.Should().Be(AnchorMatchReason.BuildableRegionFallback);
    }

    [Fact]
    public void ResolveBuildableRegion_UsesBoundsCenterWhenCentroidMissing()
    {
        WillieRoomAnchor region = Anchor("area:0", RoomClass.BuildableRegion, 25, null) with
        {
            Bounds = new MapRect(10, 20, 14, 24)
        };
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory([region])
        };

        ResolvedAnchor resolved = AnchorResolver.ResolveBuildableRegion(briefing)
            .Should().ContainSingle().Subject;

        resolved.TargetCell.Should().Be(new MapPosition(12, 0, 22));
        resolved.MatchReason.Should().Be(AnchorMatchReason.BuildableRegionFallback);
    }

    [Fact]
    public void ResolveNear_DoesNotUseBuildableRegion()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            AnchorInventory = new WillieAnchorInventory(
            [
                Anchor("area:0", RoomClass.BuildableRegion, 25, new MapPosition(12, 0, 22))
            ])
        };

        AnchorResolver.ResolveNear(SpecWithNear("kitchen"), briefing).Should().BeEmpty();
    }

    private static WillieRoomAnchor Anchor(
        string id,
        RoomClass roomClass,
        int cells,
        MapPosition? centroid) =>
        new(id, roomClass, roomClass.ToString(), cells, centroid, []);

    private static PlacementSpec SpecWithNear(string target) =>
        new(
            Request: "starter freezer",
            Reason: "food storage",
            TargetClass: BuildingClass.Freezer,
            TargetDef: null,
            RoomClass: RoomClass.Freezer,
            CapacityNeed: new CapacityNeed(CapacityMeasure.FoodUnits, 200),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, target)],
            Power: null,
            Temperature: new TempNeed(TemperatureBand.Freezing, true),
            MaterialsOnHand: [],
            Deadline: null,
            Priority: Priority.Medium,
            Source: "Chef",
            Constraints: []);

    private static WillieBriefing StableBriefing() =>
        new(
            BriefingVersion: 1,
            Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
            GameTick: 300_000,
            MapId: 7,
            ColonistCount: 3,
            PowerStability: new WilliePowerStabilitySummary(900, 650, 800, 1000, 250, 1, 2),
            ThermalControl: new WillieThermalControlSummary(1, 0, 0),
            FunctionalRooms: new WillieFunctionalRoomsSummary(new Dictionary<string, int>()),
            StoragePlacement: new WillieStoragePlacementSummary(1, 40),
            MaterialBottleneck: new WillieMaterialBottleneckSummary([], [], 0, 0),
            FireRisk: new WillieFireRiskSummary(0),
            StalledBuilds: new WillieStalledBuildsSummary([], 0, 0, 0, 0),
            BaseLayout: new WillieBaseLayoutSummary(3, 8),
            DataCoverage: new WillieDataCoverage(true, true, true, true, true, true, true, false));
}
