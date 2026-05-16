using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;

namespace RimBob.Tests.Coordination;

public sealed class AdviceBusTests
{
    [Fact]
    public void Publish_NotifiesAgendaSubscribers()
    {
        AdviceBus bus = new();
        List<MayorAgenda> received = [];

        bus.AgendaUpdated += e => received.Add(e.Agenda);

        MayorAgenda agenda = new(
            Version: 1, UpdatedInGameTick: "Y1Q1D1",
            GeneratedAt: DateTimeOffset.UnixEpoch,
            Posture: new MayorPosture("growth", "defensive", "go"),
            StateOfTheUnion: new Dictionary<string, string> { ["welfare"] = "All quiet." },
            UpdateNotes: "Day 1.",
            ShortTerm: [], LongTerm: [],
            CabinetDirection: new Dictionary<string, string>(),
            GuideCitations: []);

        bus.Publish(new AgendaUpdated(agenda));

        received.Should().ContainSingle().Which.Version.Should().Be(1);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        AdviceBus bus = new();
        Action act = () => bus.Publish(new AgendaUpdated(InputBuilder.Default.ToAgenda(7, "tick")));
        act.Should().NotThrow();
    }

    [Fact]
    public void PublishAdvice_RetainsActiveAdviceForReplay()
    {
        AdviceBus bus = new();
        AdviceItem item = Advice("a1", "Food");

        bus.Publish(item);

        bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("a1");
    }

    [Fact]
    public void ReplaceMinisterAdvice_ReplacesOnlyThatMinistersActiveSet()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("old_food", "Food"));
        bus.Publish(Advice("old_defense", "Defense"));
        List<AdviceSnapshot> snapshots = [];
        List<AdviceItem> published = [];
        bus.AdviceSnapshotPublished += snapshots.Add;
        bus.AdvicePublished += published.Add;

        bus.ReplaceMinisterAdvice("Food", [Advice("new_food", "Food")], "Food is low but actionable.");

        bus.ActiveAdvice().Select(a => a.Id).Should().BeEquivalentTo(["new_food", "old_defense"]);
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().Be("Food");
        snapshots[0].StateSummary.Should().Be("Food is low but actionable.");
        snapshots[0].Advice.Should().ContainSingle().Which.Id.Should().Be("new_food");
        published.Should().ContainSingle().Which.Id.Should().Be("new_food");
    }

    [Fact]
    public void ReplaceMinisterAdvice_EmptySnapshotClearsMinisterAdvice()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("old_food", "Food"));
        bus.Publish(Advice("old_defense", "Defense"));
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.ReplaceMinisterAdvice("Food", []);

        bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("old_defense");
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().Be("Food");
        snapshots[0].Advice.Should().BeEmpty();
    }

    [Fact]
    public void ActiveSnapshot_ReplaysMinisterStateSummaries()
    {
        AdviceBus bus = new();

        bus.ReplaceMinisterAdvice("Food", [], "Food is stable.");

        AdviceSnapshot snapshot = bus.ActiveSnapshot();

        snapshot.Minister.Should().BeNull();
        snapshot.Advice.Should().BeEmpty();
        snapshot.StateSummaries.Should().ContainKey("Food").WhoseValue.Should().Be("Food is stable.");
    }

    private static AdviceItem Advice(string id, string minister) => new(
        Id: id,
        Minister: minister,
        AdviceType: "food_security",
        Priority: AdvicePriority.High,
        Title: "Food low",
        Body: "Body",
        Rationale: "Rationale",
        Steps: [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
}

internal static class InputBuilderExtensions
{
    public static MayorAgenda ToAgenda(this MayorAgendaInput input, int version, string tick) => new(
        Version: version, UpdatedInGameTick: tick,
        GeneratedAt: DateTimeOffset.UnixEpoch,
        Posture: input.Posture, StateOfTheUnion: input.StateOfTheUnion,
        UpdateNotes: input.UpdateNotes, ShortTerm: input.ShortTerm, LongTerm: input.LongTerm,
        CabinetDirection: input.CabinetDirection,
        GuideCitations: input.GuideCitations ?? []);
}
