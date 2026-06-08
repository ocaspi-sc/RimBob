using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class WillieSolverStoreTests
{
    [Fact]
    public void Latest_ReturnsNullForUnknownMinister()
    {
        WillieSolverStore sut = new();

        sut.Latest("Willie").Should().BeNull();
    }

    [Fact]
    public void Record_OverwritesLatestSnapshotByMinister()
    {
        WillieSolverStore sut = new();
        WillieSolverSnapshot first = Snapshot("first");
        WillieSolverSnapshot second = Snapshot("second");

        sut.RecordBuildingOutcome(first);
        sut.RecordBuildingOutcome(second);

        sut.Latest("Willie").Should().BeSameAs(second);
    }

    [Fact]
    public void RequestBoard_JoinsInboundRowsToRecordedOutcomes()
    {
        WillieSolverStore sut = new();
        BuildingRequest request = Request("starter freezer near kitchen");
        WillieInboundRequest inbound = new(
            request,
            SourceMinister: "Chef",
            RequestKey: WillieSolverStore.RequestKey(request));
        WillieSolverSnapshot outcome = Snapshot(request.Request, [Option("placement_freezer")]);

        sut.RecordInbound("Willie", [inbound]);
        sut.RecordBuildingOutcome(outcome);

        WillieRequestBoardRow row = sut.RequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.Should().Be(inbound);
        row.Outcome.Should().BeSameAs(outcome);
        row.Outcome!.Options.Should().ContainSingle().Which.Id.Should().Be("placement_freezer");
    }

    [Fact]
    public void TryGetFreshBuildingOutcome_ReturnsOnlyMatchingFingerprint()
    {
        WillieSolverStore sut = new();
        BuildingRequest request = Request("starter freezer near kitchen");
        WillieSolverSnapshot outcome = Snapshot(request.Request, [Option("placement_freezer")]);

        sut.RecordBuildingOutcome(outcome, inputFingerprint: "request-state-a");

        sut.TryGetFreshBuildingOutcome("Willie", request, "request-state-a").Should().BeSameAs(outcome);
        sut.TryGetFreshBuildingOutcome("Willie", request, "request-state-b").Should().BeNull();
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("running")]
    [InlineData("error")]
    [InlineData("stale")]
    public void TryGetFreshBuildingOutcome_ReturnsOnlyReusableTerminalOutcomes(string status)
    {
        WillieSolverStore sut = new();
        BuildingRequest request = Request("starter freezer near kitchen");

        sut.RecordBuildingOutcome(Snapshot(request.Request, status: status), inputFingerprint: "request-state-a");

        sut.TryGetFreshBuildingOutcome("Willie", request, "request-state-a").Should().BeNull();
    }

    [Fact]
    public void RecordInbound_HidesRowsButKeepsFreshCacheWhenRequestsLeaveBoard()
    {
        WillieSolverStore sut = new();
        BuildingRequest freezerRequest = Request("starter freezer near kitchen");
        BuildingRequest workshopRequest = Request(
            request: "starter workshop near storage",
            targetClass: BuildingClass.ProductionBench,
            targetDef: "ElectricTailoringBench",
            roomClass: RoomClass.Workshop);
        WillieInboundRequest freezerInbound = Inbound(freezerRequest, "Chef");
        WillieInboundRequest workshopInbound = Inbound(workshopRequest, "Industry");

        sut.RecordInbound("Willie", [freezerInbound, workshopInbound]);
        sut.RecordBuildingOutcome(Snapshot(freezerRequest.Request), inputFingerprint: "freezer-state");
        sut.RecordBuildingOutcome(Snapshot(
            workshopRequest.Request,
            targetClass: "ProductionBench",
            targetDef: "ElectricTailoringBench",
            roomClass: "Workshop"), inputFingerprint: "workshop-state");
        sut.RecordInbound("Willie", [workshopInbound]);
        sut.RequestBoard("Willie").Should().ContainSingle()
            .Which.Inbound.Request.Request.Should().Be(workshopRequest.Request);
        sut.TryGetFreshBuildingOutcome("Willie", freezerRequest, "freezer-state").Should().NotBeNull();
        sut.RecordInbound("Willie", [freezerInbound, workshopInbound]);

        IReadOnlyList<WillieRequestBoardRow> rows = sut.RequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows[0].Inbound.Request.Request.Should().Be(freezerRequest.Request);
        rows[0].Outcome.Should().NotBeNull();
        rows[1].Inbound.Request.Request.Should().Be(workshopRequest.Request);
        rows[1].Outcome.Should().NotBeNull();
        sut.TryGetFreshBuildingOutcome("Willie", freezerRequest, "freezer-state").Should().NotBeNull();
    }

    [Fact]
    public void RecordInbound_PrunesDroppedQueuedRows()
    {
        WillieSolverStore sut = new();
        BuildingRequest freezerRequest = Request("starter freezer near kitchen");
        WillieInboundRequest freezerInbound = Inbound(freezerRequest, "Chef");

        sut.RecordInbound("Willie", [freezerInbound]);
        sut.RecordBuildingOutcome(Snapshot(freezerRequest.Request, status: "queued"), inputFingerprint: "freezer-state");
        sut.RecordInbound("Willie", []);

        sut.RequestBoard("Willie").Should().BeEmpty();
        sut.TryGetFreshBuildingOutcome("Willie", freezerRequest, "freezer-state").Should().BeNull();
    }

    [Fact]
    public void RecordInbound_KeepsFreshCacheForRequestsThatNeverAppearOnBoard()
    {
        WillieSolverStore sut = new();
        BuildingRequest missingKitchenRequest = Request(
            request: "starter kitchen footprint",
            targetClass: BuildingClass.ProductionBench,
            targetDef: "FueledStove",
            roomClass: RoomClass.Kitchen);

        sut.RecordBuildingOutcome(Snapshot(
            missingKitchenRequest.Request,
            targetClass: "ProductionBench",
            targetDef: "FueledStove",
            roomClass: "Kitchen"), inputFingerprint: "missing-kitchen-state");
        sut.RecordInbound("Willie", []);

        sut.RequestBoard("Willie").Should().BeEmpty();
        sut.TryGetFreshBuildingOutcome("Willie", missingKitchenRequest, "missing-kitchen-state")
            .Should().NotBeNull();
        sut.TryGetFreshBuildingOutcome("Willie", missingKitchenRequest, "changed-state")
            .Should().BeNull();
    }

    [Fact]
    public void RecordInbound_CollapsesDuplicateRequestKeys()
    {
        WillieSolverStore sut = new();
        BuildingRequest request = Request("starter freezer near kitchen");

        sut.RecordInbound("Willie", [Inbound(request, "Chef"), Inbound(request, "Welfare")]);

        WillieRequestBoardRow row = sut.RequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.SourceMinister.Should().Be("Chef");
    }

    [Fact]
    public void ZoneRequestBoard_ReturnsCurrentInboundZoneRows()
    {
        WillieSolverStore sut = new();
        ZoneRequest request = ZoneRequest("36 rice growing tiles near storage");
        WillieInboundZoneRequest inbound = ZoneInbound(request, "Chef");

        sut.RecordZoneInbound("Willie", [inbound]);

        WillieZoneRequestBoardRow row = sut.ZoneRequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.Should().Be(inbound);
        row.Outcome.Should().BeNull();
    }

    [Fact]
    public void RecordZoneInbound_CollapsesDuplicateRequestKeys()
    {
        WillieSolverStore sut = new();
        ZoneRequest request = ZoneRequest("36 rice growing tiles near storage");

        sut.RecordZoneInbound("Willie", [ZoneInbound(request, "Chef"), ZoneInbound(request, "Welfare")]);

        WillieZoneRequestBoardRow row = sut.ZoneRequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.SourceMinister.Should().Be("Chef");
        row.Outcome.Should().BeNull();
    }

    [Fact]
    public void RecordZoneInbound_HidesRowsButKeepsFreshCacheWhenRequestsLeaveBoard()
    {
        WillieSolverStore sut = new();
        ZoneRequest riceRequest = ZoneRequest("36 rice growing tiles near storage");
        ZoneRequest cornRequest = ZoneRequest("49 corn growing tiles near storage") with
        {
            PlantDef = "Plant_Corn",
            TileCount = 49
        };
        WillieInboundZoneRequest riceInbound = ZoneInbound(riceRequest, "Chef");
        WillieInboundZoneRequest cornInbound = ZoneInbound(cornRequest, "Chef");

        sut.RecordZoneInbound("Willie", [riceInbound, cornInbound]);
        sut.RecordZoneOutcome(ZoneSnapshot(riceRequest), inputFingerprint: "rice-zone-state");
        sut.RecordZoneOutcome(ZoneSnapshot(cornRequest), inputFingerprint: "corn-zone-state");
        sut.RecordZoneInbound("Willie", [cornInbound]);
        sut.ZoneRequestBoard("Willie").Should().ContainSingle()
            .Which.Inbound.Request.PlantDef.Should().Be("Plant_Corn");
        sut.TryGetFreshZoneOutcome("Willie", riceRequest, "rice-zone-state").Should().NotBeNull();
        sut.RecordZoneInbound("Willie", [riceInbound, cornInbound]);

        IReadOnlyList<WillieZoneRequestBoardRow> rows = sut.ZoneRequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(row => row.Outcome != null);
        sut.TryGetFreshZoneOutcome("Willie", riceRequest, "rice-zone-state").Should().NotBeNull();
    }

    [Fact]
    public void RecordZoneInbound_KeepsFreshCacheForRequestsThatNeverAppearOnBoard()
    {
        WillieSolverStore sut = new();
        ZoneRequest syntheticRequest = ZoneRequest("36 rice growing tiles near storage");

        sut.RecordZoneOutcome(ZoneSnapshot(syntheticRequest), inputFingerprint: "synthetic-zone-state");
        sut.RecordZoneInbound("Willie", []);

        sut.ZoneRequestBoard("Willie").Should().BeEmpty();
        sut.TryGetFreshZoneOutcome("Willie", syntheticRequest, "synthetic-zone-state")
            .Should().NotBeNull();
        sut.TryGetFreshZoneOutcome("Willie", syntheticRequest, "changed-state")
            .Should().BeNull();
    }

    [Fact]
    public void RecordZoneOutcome_JoinsLatestOutcomeToZoneBoard()
    {
        WillieSolverStore sut = new();
        ZoneRequest request = ZoneRequest("36 rice growing tiles near storage");
        sut.RecordZoneInbound("Willie", [ZoneInbound(request, "Chef")]);

        sut.RecordZoneOutcome(ZoneSnapshot(request));

        WillieZoneRequestBoardRow row = sut.ZoneRequestBoard("Willie").Should().ContainSingle().Subject;
        row.Outcome.Should().NotBeNull();
        row.Outcome!.Status.Should().Be("no_fit");
        row.Outcome.NoFit.Should().Be(nameof(NoFitReason.NoTerrainGrid));
    }

    [Fact]
    public void TryGetFreshZoneOutcome_ReturnsOnlyMatchingFingerprint()
    {
        WillieSolverStore sut = new();
        ZoneRequest request = ZoneRequest("36 rice growing tiles near storage");
        WillieZoneSolverSnapshot outcome = ZoneSnapshot(request);

        sut.RecordZoneOutcome(outcome, inputFingerprint: "zone-state-a");

        sut.TryGetFreshZoneOutcome("Willie", request, "zone-state-a").Should().BeSameAs(outcome);
        sut.TryGetFreshZoneOutcome("Willie", request, "zone-state-b").Should().BeNull();
    }

    [Fact]
    public void RequestKey_MatchesFullRequestAndReducedSnapshot()
    {
        BuildingRequest request = Request(
            request: "starter workshop near storage",
            targetClass: BuildingClass.ProductionBench,
            targetDef: "ElectricTailoringBench",
            roomClass: RoomClass.Workshop);
        WillieSolverRequestSnapshot snapshot = WillieSolverRequestSnapshot.FromRequest(request, "Industry");

        WillieSolverStore.RequestKey(request).Should().Be(WillieSolverStore.RequestKey(snapshot));
    }

    private static WillieInboundRequest Inbound(BuildingRequest request, string sourceMinister) =>
        new(
            request,
            SourceMinister: sourceMinister,
            RequestKey: WillieSolverStore.RequestKey(request));

    private static WillieInboundZoneRequest ZoneInbound(ZoneRequest request, string sourceMinister) =>
        new(
            request,
            SourceMinister: sourceMinister,
            RequestKey: WillieSolverStore.RequestKey(request));

    private static BuildingRequest Request(
        string request,
        BuildingClass targetClass = BuildingClass.Freezer,
        string? targetDef = "Cooler",
        RoomClass? roomClass = RoomClass.Freezer) =>
        new(
            Request: request,
            Reason: "test",
            TargetClass: targetClass,
            TargetDef: targetDef,
            RoomClass: roomClass,
            CapacityNeed: new CapacityNeed(CapacityMeasure.StorageStacks, 4),
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "kitchen")],
            Power: new PowerNeed(true, 200),
            Temperature: new TempNeed(TemperatureBand.Freezing, MustHold: true),
            MaterialsOnHand: [new MaterialHint("Steel", 60)],
            Urgency: Urgency.Soon,
            Deadline: new Deadline(DeadlineKind.ByDay, 4),
            Quantity: 1,
            Priority: Priority.Medium,
            RequestedFrom: "Willie");

    private static ZoneRequest ZoneRequest(string request) =>
        new(
            Request: request,
            Reason: "test",
            ZoneClass: ZoneClass.Growing,
            PlantDef: "Plant_Rice",
            TileCount: 36,
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
            Terrain: new TerrainNeed(MustSupportGrowing: true),
            Priority: Priority.High,
            RequestedFrom: "Willie");

    private static WillieSolverSnapshot Snapshot(
        string request,
        IReadOnlyList<AdviceOption>? options = null,
        string status = "options",
        string targetClass = "Freezer",
        string? targetDef = "Cooler",
        string? roomClass = "Freezer") =>
        new(
            Minister: "Willie",
            Request: new WillieSolverRequestSnapshot(
                Request: request,
                Reason: "test",
                TargetClass: targetClass,
                TargetDef: targetDef,
                RoomClass: roomClass,
                RequestedFrom: "Willie",
                SourceMinister: "Chef",
                Priority: "Medium"),
            GameTick: 123,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: new PlacementSolverReplayOutput(
                Status: status,
                NoFit: null,
                Draftable: "Ready",
                PlacementValid: "Ready",
                MaterialsReady: "Ready",
                ApplyReady: "Ready",
                Trace: null,
                ErrorType: null,
                ErrorMessage: null),
            Options: options ?? []);

    private static WillieZoneSolverSnapshot ZoneSnapshot(ZoneRequest request) =>
        new(
            Minister: "Willie",
            Request: WillieZoneRequestSnapshot.FromRequest(request, "Chef"),
            GameTick: 300_000,
            CapturedAt: DateTimeOffset.UnixEpoch,
            Output: new PlacementSolverReplayOutput(
                Status: "no_fit",
                NoFit: nameof(NoFitReason.NoTerrainGrid),
                Draftable: "Blocked",
                PlacementValid: "Blocked",
                MaterialsReady: "Ready",
                ApplyReady: "Blocked",
                Trace: null,
                ErrorType: null,
                ErrorMessage: null),
            Options: []);

    private static AdviceOption Option(string id) =>
        new(
            Id: id,
            Label: "Compact freezer",
            Summary: "Validated freezer shell near kitchen.",
            BlueprintGroup: new BlueprintGroup(
                Label: "Compact freezer",
                MapId: 7,
                Assets:
                [
                    new BlueprintAsset("wall", "Wall", "BlocksGranite", new MapCell(10, 12), 0)
                ]),
            EstimatedMaterials: [new MaterialEstimate("BlocksGranite", 5)]);
}
