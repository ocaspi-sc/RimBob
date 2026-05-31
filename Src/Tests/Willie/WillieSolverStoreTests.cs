using FluentAssertions;
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

    private static WillieSolverSnapshot Snapshot(string request) =>
        new(
            Minister: "Willie",
            Request: new WillieSolverRequestSnapshot(
                Request: request,
                Reason: "test",
                TargetClass: "Freezer",
                TargetDef: "Cooler",
                RoomClass: "Freezer",
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
                ErrorMessage: null));
}
