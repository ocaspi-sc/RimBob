using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Food;

namespace RimBob.Tests.Coordination;

public sealed class MinisterOutputStoreTests
{
    [Fact]
    public void CurrentMayorAgenda_NullBeforeFirstUpdate()
    {
        MinisterOutputStore store = new();

        store.CurrentMayorAgenda.Should().BeNull();
        store.AdviceSnapshots().Should().BeEmpty();
        store.HasAnyOutput.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMayorAsync_AssignsMonotonicVersionStartingAtOne()
    {
        MinisterOutputStore store = new();

        MayorAgenda first = await store.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "first" }, TestDate(0));
        MayorAgenda second = await store.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "second" }, TestDate(GameTime.TicksPerGameDay));
        MayorAgenda third = await store.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "third" }, TestDate(GameTime.TicksPerGameDay * 2));

        first.Version.Should().Be(1);
        second.Version.Should().Be(2);
        third.Version.Should().Be(3);
        store.CurrentMayorAgenda!.Version.Should().Be(3);
    }

    [Fact]
    public async Task UpdateMayorAsync_StampsGameDateIntoTheReturnedAgenda()
    {
        MinisterOutputStore store = new();
        GameDate date = TestDate(GameTime.TicksPerGameDay * 47);

        MayorAgenda result = await store.UpdateMayorAsync(InputBuilder.Default, date);

        result.UpdatedGameDate.Should().Be(date);
        result.UpdatedGameDate.TotalDays.Should().Be(47);
    }

    [Fact]
    public async Task LoadAsync_MissingDirectoryStartsEmpty()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore store = await MinisterOutputStore.LoadAsync(root);

            store.CurrentMayorAgenda.Should().BeNull();
            store.AdviceSnapshots().Should().BeEmpty();
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task UpdateMayorAsync_WritesSnapshotAndLoadAsync_RestoresCurrent()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore store = await MinisterOutputStore.LoadAsync(root);

            GameDate date = TestDate();
            MayorAgenda stored = await store.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "persisted" }, date);
            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);

            File.Exists(Path.Combine(root, "mayor.json")).Should().BeTrue();
            restored.CurrentMayorAgenda.Should().NotBeNull();
            restored.CurrentMayorAgenda!.Version.Should().Be(stored.Version);
            restored.CurrentMayorAgenda.UpdateNotes.Should().Be("persisted");
            restored.CurrentMayorAgenda.UpdatedGameDate.Should().Be(date);
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_RestoredStoreContinuesMayorVersioning()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore firstStore = await MinisterOutputStore.LoadAsync(root);
            await firstStore.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "v1" }, TestDate());

            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);
            MayorAgenda next = await restored.UpdateMayorAsync(InputBuilder.Default with { UpdateNotes = "v2" }, TestDate(GameTime.TicksPerGameDay));

            next.Version.Should().Be(2);
            restored.CurrentMayorAgenda!.Version.Should().Be(2);
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task AdviceSnapshotFlush_WritesAndReloadsFeederSnapshot()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore store = await MinisterOutputStore.LoadAsync(root);
            AdviceChainModel chain = Chain();
            AdviceSnapshot snapshot = new(
                Minister: "Chef",
                Advice: [Advice("food", "Chef")],
                StateSummary: "Food is stable.",
                Chain: chain);

            store.QueueAdviceSnapshot(snapshot);
            await store.FlushPendingAdviceAsync();
            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);

            File.Exists(Path.Combine(root, "chef.json")).Should().BeTrue();
            AdviceSnapshot? reloaded = restored.GetAdviceSnapshot("chef");
            reloaded.Should().NotBeNull();
            reloaded!.Advice.Should().ContainSingle().Which.Id.Should().Be("food");
            reloaded.Minister.Should().Be("Chef");
            reloaded.Advice.Should().ContainSingle().Which.Minister.Should().Be("Chef");
            reloaded.StateSummary.Should().Be("Food is stable.");
            reloaded.Chain.Should().NotBeNull();
            restored.GetAdviceSnapshot("food").Should().BeSameAs(reloaded);
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_MigratesObsoleteFoodCookPriorityActionIntoFlagRequest()
    {
        string root = NewSnapshotRoot();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "food.json"),
                """
                {
                  "schema_version": 2,
                  "minister": "food",
                  "output_kind": "advice_snapshot",
                  "persisted_at": "2026-01-01T00:00:00+00:00",
                  "generation": 1,
                  "payload": {
                    "minister": "Food",
                    "advice": [
                      {
                        "id": "old_food",
                        "minister": "Food",
                        "priority": "high",
                        "title": "Food low",
                        "body": "Body",
                        "rationale": "Rationale",
                        "actions": [
                          {
                            "kind": "set_priority",
                            "instruction": "Put the best cook on Cook work until simple meals are stocked.",
                            "owner": "Labor",
                            "work_type": "cook",
                            "skill": "Cooking",
                            "reason": "raw food must become meals during an urgent shortage",
                            "icon": { "kind": "item", "id": "MealSimple" }
                          }
                        ],
                        "guide_citation_ids": [],
                        "issued_at": "2026-01-01T00:00:00+00:00",
                        "expires_at": "2026-01-01T04:00:00+00:00",
                        "issued_game_date": {
                          "raw_rim_world_date": "5th of Aprimay, 5500, 14h",
                          "rim_world_year": 5500,
                          "quadrum": "Aprimay",
                          "quadrum_day": 5,
                          "hour": 14,
                          "game_tick": 300000,
                          "total_days": 5,
                          "completed_days": 5,
                          "colony_day": 6,
                          "colony_year": 1,
                          "day_of_year": 6,
                          "label": "Y1 D6, Aprimay 5, 14h"
                        },
                        "issued_game_tick": 300000
                      }
                    ],
                    "state_summary": "Stored Food state."
                  }
                }
                """);

            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);

            AdviceSnapshot? snapshot = restored.GetAdviceSnapshot("chef");
            snapshot.Should().NotBeNull();
            AdviceItem advice = snapshot!.Advice.Should().ContainSingle().Subject;
            advice.Minister.Should().Be("Chef");
            advice.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
            snapshot.Flags.Should().ContainSingle()
                .Which.SourceMinister.Should().Be("Chef");
            snapshot.Flags.Should().ContainSingle()
                .Which.LaborRequests.Should().ContainSingle()
                .Which.WorkType.Should().Be(WorkType.Cook);
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task CabinetDirection_ReloadsForFeederContext()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore store = await MinisterOutputStore.LoadAsync(root);
            await store.UpdateMayorAsync(InputBuilder.Default with
            {
                CabinetDirection = new Dictionary<string, string> { ["food"] = "Keep emergency meals covered." }
            }, TestDate());

            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);
            MinisterBriefingContext context = Chef.BuildContext(restored.CurrentMayorAgenda);

            context.AgendaDirection.Should().Be("Keep emergency meals covered.");
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    [Fact]
    public async Task LoadAsync_SchemaMismatchFailsLoudly()
    {
        string root = NewSnapshotRoot();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "mayor.json"),
                """
                {
                  "schema_version": 999,
                  "minister": "mayor",
                  "output_kind": "mayor_agenda",
                  "persisted_at": "2026-01-01T00:00:00+00:00",
                  "generation": 1,
                  "payload": {}
                }
                """);

            Func<Task> act = () => MinisterOutputStore.LoadAsync(root);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Unsupported minister output snapshot schema 999*");
        }
        finally
        {
            CleanupSnapshot(root);
        }
    }

    private static string NewSnapshotRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "rimbob-minister-output-store-tests",
            Guid.NewGuid().ToString("N"),
            "ministers");

    private static void CleanupSnapshot(string root)
    {
        string? directory = Path.GetDirectoryName(root);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static AdviceItem Advice(string id, string minister) => new(
        Id: id,
        Minister: minister,
        Priority: AdvicePriority.High,
        Title: "Food low",
        Body: "Body",
        Rationale: "Rationale",
        Actions: [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    private static AdviceChainModel Chain() => new(
    [
        new AdviceChainPath("Grow path",
        [
            new AdviceChainStep("grow.trigger", "Food buffer", "4.0 days", AdviceChainStepStatus.Trigger)
        ])
    ]);

    private static GameDate TestDate(long gameTick = 300_000) =>
        GameTime.Create("5th of Aprimay, 5500, 14h", gameTick, 5500, "Aprimay", 5, 14);
}

internal static class InputBuilder
{
    public static MayorAgendaInput Default { get; } = new(
        Posture: new MayorPosture("consolidation", "defensive", "Hold steady."),
        StateOfTheUnion: new Dictionary<string, string>
        {
            ["food"] = "Food covers 18 days, no harvest pressure.",
            ["defense"] = "No active threats, walls intact.",
            ["welfare"] = "Mood 72%, no break risks.",
        },
        UpdateNotes: "Quiet day. Carrying forward.",
        ShortTerm: [],
        LongTerm: [],
        CabinetDirection: new Dictionary<string, string>());
}
