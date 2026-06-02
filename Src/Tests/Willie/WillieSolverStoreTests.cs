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

        sut.Record(first);
        sut.Record(second);

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
        sut.Record(outcome);

        WillieRequestBoardRow row = sut.RequestBoard("Willie").Should().ContainSingle().Subject;
        row.Inbound.Should().Be(inbound);
        row.Outcome.Should().BeSameAs(outcome);
        row.Outcome!.Options.Should().ContainSingle().Which.Id.Should().Be("placement_freezer");
    }

    [Fact]
    public void RecordInbound_PrunesOutcomesWhenRequestsLeaveBoard()
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
        sut.Record(Snapshot(freezerRequest.Request));
        sut.Record(Snapshot(
            workshopRequest.Request,
            targetClass: "ProductionBench",
            targetDef: "ElectricTailoringBench",
            roomClass: "Workshop"));
        sut.RecordInbound("Willie", [workshopInbound]);
        sut.RecordInbound("Willie", [freezerInbound, workshopInbound]);

        IReadOnlyList<WillieRequestBoardRow> rows = sut.RequestBoard("Willie");
        rows.Should().HaveCount(2);
        rows[0].Inbound.Request.Request.Should().Be(freezerRequest.Request);
        rows[0].Outcome.Should().BeNull();
        rows[1].Inbound.Request.Request.Should().Be(workshopRequest.Request);
        rows[1].Outcome.Should().NotBeNull();
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
            Priority: AdvicePriority.Medium,
            RequestedFrom: "Willie");

    private static WillieSolverSnapshot Snapshot(
        string request,
        IReadOnlyList<AdviceOption>? options = null,
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
                Status: "options",
                NoFit: null,
                Draftable: "Ready",
                PlacementValid: "Ready",
                MaterialsReady: "Ready",
                ApplyReady: "Ready",
                Trace: null,
                ErrorType: null,
                ErrorMessage: null),
            Options: options ?? []);

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
