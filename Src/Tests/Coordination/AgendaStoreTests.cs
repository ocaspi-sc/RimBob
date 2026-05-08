using FluentAssertions;
using RimAI.Coordination;
using RimAI.Core.Advice;

namespace RimAI.Tests.Coordination;

public sealed class AgendaStoreTests
{
    [Fact]
    public void Current_NullBeforeFirstUpdate()
    {
        AgendaStore store = new();
        store.Current.Should().BeNull();
        store.History.Should().BeEmpty();
    }

    [Fact]
    public void Update_AssignsMonotonicVersionStartingAtOne()
    {
        AgendaStore store = new();

        MayorAgenda first  = store.Update(InputBuilder.Default with { UpdateNotes = "first"  }, "Y1Aprimay D1");
        MayorAgenda second = store.Update(InputBuilder.Default with { UpdateNotes = "second" }, "Y1Aprimay D2");
        MayorAgenda third  = store.Update(InputBuilder.Default with { UpdateNotes = "third"  }, "Y1Aprimay D3");

        first.Version.Should().Be(1);
        second.Version.Should().Be(2);
        third.Version.Should().Be(3);
        store.Current!.Version.Should().Be(3);
    }

    [Fact]
    public void Update_StampsTickIntoTheReturnedAgenda()
    {
        AgendaStore store = new();

        MayorAgenda result = store.Update(InputBuilder.Default, "Y1Septober D7");

        result.UpdatedInGameTick.Should().Be("Y1Septober D7");
    }

    [Fact]
    public void Update_PushesPreviousIntoHistoryNewestFirst()
    {
        AgendaStore store = new();

        store.Update(InputBuilder.Default with { UpdateNotes = "v1" }, "tickA");
        store.Update(InputBuilder.Default with { UpdateNotes = "v2" }, "tickB");
        store.Update(InputBuilder.Default with { UpdateNotes = "v3" }, "tickC");

        store.History.Should().HaveCount(2);
        store.History[0].UpdateNotes.Should().Be("v2");
        store.History[1].UpdateNotes.Should().Be("v1");
        store.Current!.UpdateNotes.Should().Be("v3");
    }

    [Fact]
    public void History_CapsAt30()
    {
        AgendaStore store = new();
        for (int i = 1; i <= 35; i++)
            store.Update(InputBuilder.Default with { UpdateNotes = $"v{i}" }, $"tick{i}");

        store.History.Count.Should().Be(30);
        store.Current!.UpdateNotes.Should().Be("v35");
        store.History[0].UpdateNotes.Should().Be("v34");
        store.History[^1].UpdateNotes.Should().Be("v5");
    }
}

internal static class InputBuilder
{
    public static MayorAgendaInput Default { get; } = new(
        Posture:           new MayorPosture("consolidation", "defensive", "Hold steady."),
        StateOfTheUnion:   new Dictionary<string, string>
        {
            ["agriculture"] = "Food covers 18 days, no harvest pressure.",
            ["defense"]     = "No active threats, walls intact.",
            ["welfare"]     = "Mood 72%, no break risks.",
        },
        UpdateNotes:       "Quiet day. Carrying forward.",
        ShortTerm:         [],
        LongTerm:          [],
        MinisterDirection: new Dictionary<string, string>()
    );
}
