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
        WillieBriefing briefing = StableBriefing();
        RuleRun result = new Rules().Evaluate(briefing);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Trace.Should().Be("maintain_build_program");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "maintain_build_program" &&
            row.Outcome == RuleOutcome.Selected);
        AssertAllRulesCatalog(decision);
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

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("power_net_deficit");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_power_net_deficit");
        advice.Priority.Should().Be(Priority.High);
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);
        decision.Effects.OfType<Advise>().Should().ContainSingle(effect => effect.Rule == "power_net_deficit");
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

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("low_battery_reserve");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_low_battery_reserve");
        advice.Priority.Should().Be(Priority.Medium);
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

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("backlog_material_gap");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_backlog_material_gap");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.RequestResource);
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.ItemRequests.Should().NotBeNull();
        ItemRequest request = flag.ItemRequests!.Should().Contain(request => request.ItemDef == "Steel" && request.Quantity == 80).Subject;
        RuleEvaluationTrace trace = decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "backlog_material_gap" &&
            row.Outcome == RuleOutcome.Selected).Subject;
        trace.Emissions.Should().Contain(emission =>
            emission.Kind == "advise" &&
            emission.Label == advice.Title &&
            emission.Priority == Priority.High);
        trace.Emissions.Should().Contain(emission =>
            emission.Kind == "request_item" &&
            emission.Label == request.Request &&
            emission.Priority == Priority.High &&
            emission.To == "Industry" &&
            emission.TargetDef == "Steel" &&
            emission.WorkType == null);
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

        RuleRun result = rules.Evaluate(briefing);

        ProjectedRuleRun decision = Project(result, briefing, FixedNow);
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

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("frame_blocked_by_material");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_frame_blocked_by_material");
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

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("kitchen_missing");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_kitchen_missing");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);
    }

    [Fact]
    public void InboundFreezingRequest_EmitsThermalControlAdviceWithoutApply()
    {
        WillieBriefing briefing = StableBriefing();
        RuleRun result = new Rules().Evaluate(
            briefing,
            [FreezerRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be(Rules.BuildingRequestActiveTrace);
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_building_request_active");
        AdviceAction action = advice.Actions.Should().ContainSingle().Subject;
        action.Kind.Should().Be(AdviceActionKind.PlaceBlueprint);
        action.Apply.Should().BeNull();
    }

    [Fact]
    public void InboundNonFreezerRequest_EmitsGeneralBuildingRequestAdvice()
    {
        WillieBriefing briefing = StableBriefing();
        RuleRun result = new Rules().Evaluate(
            briefing,
            [WorkshopRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be(Rules.BuildingRequestActiveTrace);
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("willie_building_request_active");
        advice.Title.Should().Be("Workshop request needs Willie placement");
        Rules.TryGetPlacementRequest(decision.Trace, briefing, [WorkshopRequest()], out BuildingRequest request)
            .Should().BeTrue();
        request.RoomClass.Should().Be(RoomClass.Workshop);
    }

    [Fact]
    public void InboundFreezingRequest_WithMissingKitchen_EmitsRequestAndKitchenAdvice()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };

        RuleRun result = new Rules().Evaluate(
            briefing,
            [FreezerRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be("rules:building_request_active+kitchen_missing");
        decision.Advice.Select(advice => advice.Id).Should().Equal(
            "willie_building_request_active",
            "willie_kitchen_missing");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == Rules.BuildingRequestActiveTrace &&
            row.Outcome == RuleOutcome.Selected);
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "kitchen_missing" &&
            row.Outcome == RuleOutcome.Selected);
    }

    [Fact]
    public void InboundFreezingRequestNearMissingKitchen_DefersToKitchenPrerequisite()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };

        BuildingRequest freezerNearKitchen = FreezerRequest() with
        {
            Adjacency = [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")]
        };

        RuleRun result = new Rules().Evaluate(
            briefing,
            [freezerNearKitchen]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be("kitchen_missing");
        decision.Advice.Should().ContainSingle()
            .Which.Id.Should().Be("willie_kitchen_missing");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == Rules.BuildingRequestActiveTrace &&
            row.Outcome == RuleOutcome.NotMatched &&
            row.Conditions.Contains("depends on a missing kitchen"));
        Rules.TryGetPlacementRequest(decision.Trace, briefing, [freezerNearKitchen], out BuildingRequest request)
            .Should().BeTrue();
        request.RoomClass.Should().Be(RoomClass.Kitchen);
    }

    [Fact]
    public void InboundKitchenRequestPreemptsDependentFreezerRequest()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };
        BuildingRequest freezerNearKitchen = FreezerRequest() with
        {
            Adjacency = [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")]
        };

        RuleRun result = new Rules().Evaluate(
            briefing,
            [freezerNearKitchen, KitchenRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be("rules:building_request_active+kitchen_missing");
        AdviceItem advice = decision.Advice.Should()
            .Contain(item => item.Id == "willie_building_request_active")
            .Which;
        advice.Title.Should().Be("Kitchen request needs Willie placement");
        decision.Advice.Should().Contain(item => item.Id == "willie_kitchen_missing");
        Rules.TryGetPlacementRequest(Rules.BuildingRequestActiveTrace, briefing, [freezerNearKitchen, KitchenRequest()], out BuildingRequest request)
            .Should().BeTrue();
        request.RoomClass.Should().Be(RoomClass.Kitchen);
        request.TargetClass.Should().Be(BuildingClass.ProductionBench);
    }

    [Fact]
    public void InboundKitchenRequest_DoesNotPreemptHigherPriorityIndependentBedRequest()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };

        RuleRun result = new Rules().Evaluate(
            briefing,
            [KitchenRequest(), BedRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be("rules:building_request_active+kitchen_missing");
        Rules.TryGetPlacementRequest(Rules.BuildingRequestActiveTrace, briefing, [KitchenRequest(), BedRequest()], out BuildingRequest request)
            .Should().BeTrue();
        request.TargetClass.Should().Be(BuildingClass.Bed);
        request.RoomClass.Should().Be(RoomClass.Barracks);
        request.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void HardBuildBlocker_AndInboundRequestBothEmit()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            MaterialBottleneck = new WillieMaterialBottleneckSummary(
                BacklogGroups: [],
                MissingMaterials: [new MaterialCount("Steel", 80)],
                BlockedCount: 0,
                DisallowedCount: 0)
        };

        RuleRun result = new Rules().Evaluate(
            briefing,
            [FreezerRequest()]);

        ProjectedRuleRun decision = Project(result, briefing);
        decision.Trace.Should().Be("rules:backlog_material_gap+building_request_active");
        decision.Advice.Select(advice => advice.Id).Should().Equal(
            "willie_backlog_material_gap",
            "willie_building_request_active");
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == Rules.BuildingRequestActiveTrace &&
            row.Outcome == RuleOutcome.Selected);
    }

    [Fact]
    public void MissingKitchenTrace_SynthesizesSolverRequest()
    {
        WillieBriefing briefing = StableBriefing() with
        {
            FunctionalRooms = FunctionalRooms(RoomClass.Hospital, RoomClass.Storage)
        };

        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Trace.Should().Be("kitchen_missing");
        Rules.TryGetPlacementRequest(decision.Trace, briefing, [], out BuildingRequest request)
            .Should().BeTrue();
        request.RoomClass.Should().Be(RoomClass.Kitchen);
        request.TargetClass.Should().Be(BuildingClass.ProductionBench);
        request.Adjacency.Should().ContainSingle()
            .Which.Target.Should().Be("storage");
    }

    private static ProjectedRuleRun Evaluate(WillieBriefing briefing)
    {
        RuleRun result = new Rules().Evaluate(briefing);
        return Project(result, briefing);
    }

    private static ProjectedRuleRun Project(RuleRun run, WillieBriefing briefing, DateTimeOffset? now = null) =>
        run.ProjectFor("Willie", "construction", briefing.BriefingVersion, briefing.Date, briefing.GameTick, now ?? FixedNow);

    private static void AssertAllRulesCatalog(ProjectedRuleRun decision)
    {
        decision.Diagnostics.Should().NotBeNull();
        decision.Diagnostics!.AllRules.Should().HaveCount(11);
        decision.Diagnostics.AllRules.Select(row => row.Rule).Should().Equal(
            "power_net_deficit",
            "low_battery_reserve",
            "backlog_material_gap",
            "frame_blocked_by_material",
            Rules.BuildingRequestActiveTrace,
            Rules.ZoneRequestActiveTrace,
            "kitchen_missing",
            "hospital_missing",
            "storage_room_missing",
            "cooler_missing",
            "maintain_build_program");
    }

    private static BuildingRequest FreezerRequest() =>
        new(
            Request: "starter freezer near kitchen",
            Reason: "Chef needs cold storage",
            TargetClass: BuildingClass.Freezer,
            TargetDef: "Cooler",
            RoomClass: RoomClass.Freezer,
            Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
            Priority: Priority.High,
            RequestedFrom: "Willie");

    private static BuildingRequest KitchenRequest() =>
        new(
            Request: "starter kitchen cooking station",
            Reason: "Chef needs cooking throughput before freezer adjacency matters",
            TargetClass: BuildingClass.ProductionBench,
            TargetDef: "Campfire",
            RoomClass: RoomClass.Kitchen,
            CapacityNeed: new CapacityNeed(CapacityMeasure.WorkSlots, 1),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
            Priority: Priority.Medium,
            RequestedFrom: "Willie");

    private static BuildingRequest BedRequest() =>
        new(
            Request: "add 3 beds in a roofed barracks",
            Reason: "3 colonists lack bed capacity",
            TargetClass: BuildingClass.Bed,
            TargetDef: "Bed",
            RoomClass: RoomClass.Barracks,
            CapacityNeed: new CapacityNeed(CapacityMeasure.Beds, 3),
            Priority: Priority.High,
            RequestedFrom: "Willie");

    private static BuildingRequest WorkshopRequest() =>
        new(
            Request: "starter workshop near storage",
            Reason: "Industry needs covered production benches",
            TargetClass: BuildingClass.ProductionBench,
            TargetDef: "ElectricTailoringBench",
            RoomClass: RoomClass.Workshop,
            CapacityNeed: new CapacityNeed(CapacityMeasure.WorkSlots, 1),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
            Priority: Priority.Medium,
            RequestedFrom: "Willie");

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
