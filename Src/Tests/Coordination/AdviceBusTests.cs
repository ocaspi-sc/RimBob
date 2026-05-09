using FluentAssertions;
using RimAI.Coordination;
using RimAI.Core.Advice;

namespace RimAI.Tests.Coordination;

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
            MinisterDirection: new Dictionary<string, string>(),
            Citations: []);

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
}

internal static class InputBuilderExtensions
{
    public static MayorAgenda ToAgenda(this MayorAgendaInput input, int version, string tick) => new(
        Version: version, UpdatedInGameTick: tick,
        GeneratedAt: DateTimeOffset.UnixEpoch,
        Posture: input.Posture, StateOfTheUnion: input.StateOfTheUnion,
        UpdateNotes: input.UpdateNotes, ShortTerm: input.ShortTerm, LongTerm: input.LongTerm,
        MinisterDirection: input.MinisterDirection,
        Citations: input.Citations ?? []);
}
