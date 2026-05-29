using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class WillieRulesTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StableBuildProgram_ReturnsEmptyDecision()
    {
        RulesResult result = new Rules().Evaluate(StableBriefing(), ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Trace.Should().Be("maintain_build_program");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "maintain_build_program" &&
            row.Outcome == "selected");
    }

    [Fact]
    public void PowerDeficit_EmitsPowerStabilityAdvice()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            PowerStability = new WilliePowerStabilitySummary(
                ProductionW: 400,
                ConsumptionW: 650,
                StoredWd: 0,
                CapacityWd: 0,
                NetW: -250,
                GeneratorCount: 1,
                BatteryCount: 0)
        };

        Decision decision = Evaluate(briefing);

        decision.Trace.Should().Be("power_net_deficit");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("power_stability");
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);
        decision.Diagnostics!.EmittedAdvice.Should().ContainSingle(row =>
            row.Rule == "power_net_deficit" &&
            row.AdviceId == advice.Id);
    }

    [Fact]
    public void LowBatteryReserve_EmitsPowerStabilityAdvice()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            PowerStability = new WilliePowerStabilitySummary(
                ProductionW: 700,
                ConsumptionW: 500,
                StoredWd: 100,
                CapacityWd: 1000,
                NetW: 200,
                GeneratorCount: 1,
                BatteryCount: 2)
        };

        Decision decision = Evaluate(briefing);

        decision.Trace.Should().Be("low_battery_reserve");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("power_stability");
        advice.Priority.Should().Be(AdvicePriority.Medium);
    }

    [Fact]
    public void MissingMaterials_EmitsMaterialBottleneckAdviceAndItemFlag()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            MaterialBottleneck = new WillieMaterialBottleneckSummary(
                BacklogGroups: [],
                MissingMaterials: [new MaterialCount("Steel", 80), new MaterialCount("ComponentIndustrial", 2)],
                BlockedCount: 0,
                DisallowedCount: 0)
        };

        Decision decision = Evaluate(briefing);

        decision.Trace.Should().Be("backlog_material_gap");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("material_bottleneck");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.RequestResource);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.ItemRequests.Should().NotBeNull();
        flag.ItemRequests!.Should().Contain(request => request.ItemDef == "Steel" && request.Quantity == 80);
    }

    [Fact]
    public void EmittedAdviceAndFlagsUseInjectedClock()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            MaterialBottleneck = new WillieMaterialBottleneckSummary(
                BacklogGroups: [],
                MissingMaterials: [new MaterialCount("Steel", 80)],
                BlockedCount: 0,
                DisallowedCount: 0)
        };
        Rules rules = new(new FixedTimeProvider(FixedNow));

        RulesResult result = rules.Evaluate(briefing, ColonyContext.Default);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.IssuedAt.Should().Be(FixedNow);
        advice.ExpiresAt.Should().Be(FixedNow.AddHours(4));
        decision.Flags.Should().ContainSingle().Which.ExpiresAt.Should().Be(FixedNow.AddHours(24));
    }

    [Fact]
    public void BlockedBuildsWithoutMaterialList_EmitsStalledBuildsAdvice()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            StalledBuilds = new WillieStalledBuildsSummary(
                BacklogGroups: [],
                PendingBuildCount: 2,
                TotalWorkLeft: 300,
                BlockedCount: 2,
                DisallowedCount: 0)
        };

        Decision decision = Evaluate(briefing);

        decision.Trace.Should().Be("frame_blocked_by_material");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("stalled_builds");
        decision.Flags.Should().ContainSingle()
            .Which.Attention.Should().ContainSingle()
            .Which.Request.Should().Contain("unblock");
    }

    [Fact]
    public void MissingKitchen_EmitsFunctionalRoomsAdvice()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };

        Decision decision = Evaluate(briefing);

        decision.Trace.Should().Be("kitchen_missing");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("functional_rooms");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);
    }

    [Fact]
    public void InboundFreezingRequest_EmitsThermalControlAdviceWithoutApply()
    {
        BuildingRequest freezerRequest = new(
            Request: "starter freezer near kitchen",
            Reason: "Chef needs cold storage",
            TargetClass: BuildingClass.Freezer,
            TargetDef: "Cooler",
            RoomClass: RoomClass.Freezer,
            Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
            Priority: AdvicePriority.High,
            RequestedFrom: "Willie");

        RulesResult result = new Rules().Evaluate(
            StableBriefing(),
            ColonyContext.Default,
            [freezerRequest]);

        Decision decision = result.Should().BeOfType<Decision>().Subject;
        decision.Trace.Should().Be("freezer_request_active");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Concern.Should().Be("thermal_control");
        AdviceAction action = advice.Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.PlaceBlueprint);
        action.Apply.Should().BeNull();
    }

    private static Decision Evaluate(WillieBriefing briefing)
    {
        RulesResult result = new Rules().Evaluate(briefing, ColonyContext.Default);
        return result.Should().BeOfType<Decision>().Subject;
    }

    private static WillieBriefing StableBriefing() =>
        new(
            BriefingVersion: 1,
            Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
            GameTick: 300_000,
            MapId: 7,
            ColonistCount: 3,
            PowerStability: new WilliePowerStabilitySummary(
                ProductionW: 900,
                ConsumptionW: 650,
                StoredWd: 800,
                CapacityWd: 1000,
                NetW: 250,
                GeneratorCount: 1,
                BatteryCount: 2),
            ThermalControl: new WillieThermalControlSummary(
                CoolerCount: 1,
                HeaterCount: 0,
                FreezerAnchorCount: 0),
            FunctionalRooms: FunctionalRooms(RoomClass.Kitchen, RoomClass.Hospital, RoomClass.Storage),
            StoragePlacement: new WillieStoragePlacementSummary(
                StockpileZones: 1,
                StockpileCells: 40),
            MaterialBottleneck: new WillieMaterialBottleneckSummary([], [], 0, 0),
            FireRisk: new WillieFireRiskSummary(WoodStructureCount: 0),
            StalledBuilds: new WillieStalledBuildsSummary([], 0, 0, 0, 0),
            BaseLayout: new WillieBaseLayoutSummary(RoomCount: 3, BuildingCount: 8),
            DataCoverage: new WillieDataCoverage(
                HasLiveState: true,
                HasRooms: true,
                HasBuildings: true,
                HasPower: true,
                HasStockpiles: true,
                HasConstructionBacklog: true,
                HasAnchorInventory: true,
                HasReachability: true));

    private static WillieFunctionalRoomsSummary FunctionalRooms(params RoomClass[] classes)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (RoomClass roomClass in classes)
            counts[roomClass.ToString()] = counts.TryGetValue(roomClass.ToString(), out int current) ? current + 1 : 1;

        return new WillieFunctionalRoomsSummary(counts);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
