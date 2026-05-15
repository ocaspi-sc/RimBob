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
    public async Task UpdateAsync_AssignsMonotonicVersionStartingAtOne()
    {
        AgendaStore store = new();

        MayorAgenda first  = await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "first"  }, "Y1Aprimay D1");
        MayorAgenda second = await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "second" }, "Y1Aprimay D2");
        MayorAgenda third  = await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "third"  }, "Y1Aprimay D3");

        first.Version.Should().Be(1);
        second.Version.Should().Be(2);
        third.Version.Should().Be(3);
        store.Current!.Version.Should().Be(3);
    }

    [Fact]
    public async Task UpdateAsync_StampsTickIntoTheReturnedAgenda()
    {
        AgendaStore store = new();

        MayorAgenda result = await store.UpdateAsync(InputBuilder.Default, "Y1Septober D7");

        result.UpdatedInGameTick.Should().Be("Y1Septober D7");
    }

    [Fact]
    public async Task UpdateAsync_PushesPreviousIntoHistoryNewestFirst()
    {
        AgendaStore store = new();

        await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "v1" }, "tickA");
        await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "v2" }, "tickB");
        await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "v3" }, "tickC");

        store.History.Should().HaveCount(2);
        store.History[0].UpdateNotes.Should().Be("v2");
        store.History[1].UpdateNotes.Should().Be("v1");
        store.Current!.UpdateNotes.Should().Be("v3");
    }

    [Fact]
    public async Task History_CapsAt30()
    {
        AgendaStore store = new();
        for (int i = 1; i <= 35; i++)
            await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = $"v{i}" }, $"tick{i}");

        store.History.Count.Should().Be(30);
        store.Current!.UpdateNotes.Should().Be("v35");
        store.History[0].UpdateNotes.Should().Be("v34");
        store.History[^1].UpdateNotes.Should().Be("v5");
    }

    [Fact]
    public async Task LoadAsync_MissingFileStartsEmpty()
    {
        string path = NewSnapshotPath();
        try
        {
            AgendaStore store = await AgendaStore.LoadAsync(path);

            store.Current.Should().BeNull();
            store.History.Should().BeEmpty();
        }
        finally
        {
            CleanupSnapshot(path);
        }
    }

    [Fact]
    public async Task UpdateAsync_WritesSnapshotAndLoadAsync_RestoresCurrent()
    {
        string path = NewSnapshotPath();
        try
        {
            AgendaStore store = await AgendaStore.LoadAsync(path);

            MayorAgenda stored = await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = "persisted" }, "tickA");
            AgendaStore restored = await AgendaStore.LoadAsync(path);

            File.Exists(path).Should().BeTrue();
            restored.Current.Should().NotBeNull();
            restored.Current!.Version.Should().Be(stored.Version);
            restored.Current.UpdateNotes.Should().Be("persisted");
            restored.Current.UpdatedInGameTick.Should().Be("tickA");
        }
        finally
        {
            CleanupSnapshot(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RestoredStoreContinuesVersioning()
    {
        string path = NewSnapshotPath();
        try
        {
            AgendaStore firstStore = await AgendaStore.LoadAsync(path);
            await firstStore.UpdateAsync(InputBuilder.Default with { UpdateNotes = "v1" }, "tick1");

            AgendaStore restored = await AgendaStore.LoadAsync(path);
            MayorAgenda next = await restored.UpdateAsync(InputBuilder.Default with { UpdateNotes = "v2" }, "tick2");

            next.Version.Should().Be(2);
            restored.Current!.Version.Should().Be(2);
            restored.History.Should().ContainSingle().Which.UpdateNotes.Should().Be("v1");
        }
        finally
        {
            CleanupSnapshot(path);
        }
    }

    [Fact]
    public async Task LoadAsync_RestoresHistoryNewestFirstAndCapped()
    {
        string path = NewSnapshotPath();
        try
        {
            AgendaStore store = await AgendaStore.LoadAsync(path);
            for (int i = 1; i <= 35; i++)
                await store.UpdateAsync(InputBuilder.Default with { UpdateNotes = $"v{i}" }, $"tick{i}");

            AgendaStore restored = await AgendaStore.LoadAsync(path);

            restored.Current!.UpdateNotes.Should().Be("v35");
            restored.History.Should().HaveCount(30);
            restored.History[0].UpdateNotes.Should().Be("v34");
            restored.History[^1].UpdateNotes.Should().Be("v5");
        }
        finally
        {
            CleanupSnapshot(path);
        }
    }

    private static string NewSnapshotPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "rimai-agenda-store-tests",
            Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "agenda-store.json");
    }

    private static void CleanupSnapshot(string snapshotPath)
    {
        string? directory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

internal static class InputBuilder
{
    public static MayorAgendaInput Default { get; } = new(
        Posture:           new MayorPosture("consolidation", "defensive", "Hold steady."),
        StateOfTheUnion:   new Dictionary<string, string>
        {
            ["food"] = "Food covers 18 days, no harvest pressure.",
            ["defense"]     = "No active threats, walls intact.",
            ["welfare"]     = "Mood 72%, no break risks.",
        },
        UpdateNotes:       "Quiet day. Carrying forward.",
        ShortTerm:         [],
        LongTerm:          [],
        CabinetDirection: new Dictionary<string, string>()
    );
}
