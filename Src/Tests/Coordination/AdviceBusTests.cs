using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

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
    public void ReplaceMinisterAdvice_PublishesAndReplaysMinisterChain()
    {
        AdviceBus bus = new();
        AdviceChainModel chain = Chain();
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.ReplaceMinisterAdvice("Food", [Advice("new_food", "Food")], "Food is low.", chain);

        snapshots.Should().ContainSingle();
        snapshots[0].Chain.Should().BeSameAs(chain);
        AdviceSnapshot active = bus.ActiveSnapshot();
        active.Chains.Should().ContainKey("Food").WhoseValue.Should().BeSameAs(chain);
    }

    [Fact]
    public void ReplaceMinisterAdvice_PublishesAndReplaysMinisterFlags()
    {
        AdviceBus bus = new();
        AgentFlag flag = Flag("food:emergency", "Food");
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.ReplaceMinisterAdvice("Food", [Advice("new_food", "Food")], "Food is low.", flags: [flag]);

        snapshots.Should().ContainSingle();
        snapshots[0].Flags.Should().ContainSingle().Which.Id.Should().Be("food:emergency");
        AdviceSnapshot active = bus.ActiveSnapshot();
        active.Flags.Should().ContainSingle().Which.LaborRequests.Should().ContainSingle().Which.WorkType.Should().Be(WorkType.Cook);
    }

    [Fact]
    public async Task ReplaceMinisterAdvice_CoalescesLatestSnapshotForPersistence()
    {
        string root = NewSnapshotRoot();
        try
        {
            MinisterOutputStore store = await MinisterOutputStore.LoadAsync(root);
            AdviceBus bus = new(store);

            bus.ReplaceMinisterAdvice("Food", [Advice("old_food", "Food")], "Old summary.");
            bus.ReplaceMinisterAdvice("Food", [Advice("new_food", "Food")], "New summary.");
            await store.FlushPendingAdviceAsync();

            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);
            AdviceSnapshot? snapshot = restored.GetAdviceSnapshot("food");

            snapshot.Should().NotBeNull();
            snapshot!.Advice.Should().ContainSingle().Which.Id.Should().Be("new_food");
            snapshot.StateSummary.Should().Be("New summary.");
        }
        finally
        {
            CleanupSnapshot(root);
        }
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
    public void Hydrate_ReplaysExpiredLatestAdviceForInspection()
    {
        AdviceBus bus = new();
        AdviceItem expired = Advice("old_food", "Food") with
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        bus.Hydrate([new AdviceSnapshot("Food", [expired], "Stored Food state.")]);

        bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("old_food");
        bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Food").WhoseValue.Should().Be("Stored Food state.");
    }

    [Fact]
    public void Hydrate_MigratesObsoleteFoodCookPriorityActionIntoFlagRequest()
    {
        AdviceBus bus = new();
        AdviceItem stale = Advice("old_food", "Food", [CookPriorityAction()]);

        bus.Hydrate([new AdviceSnapshot("Food", [stale], "Stored Food state.")]);

        AdviceItem active = bus.ActiveAdvice().Should().ContainSingle().Subject;
        active.Actions.Should().NotContain(action => action.Kind == AdviceActionKind.SetPriority);
        AdviceSnapshot snapshot = bus.ActiveSnapshot();
        snapshot.Flags.Should().ContainSingle()
            .Which.LaborRequests.Should().ContainSingle()
            .Which.WorkType.Should().Be(WorkType.Cook);
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

    [Fact]
    public void RemoveAppliedAction_RemovesOnlyThatActionAndPublishesSnapshot()
    {
        AdviceBus bus = new();
        bus.ReplaceMinisterAdvice("Food", [Advice("food", "Food", [Action("harvest"), Action("bill")])], "Food summary");
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bool removed = bus.RemoveAppliedAction("food", 1);

        removed.Should().BeTrue();
        AdviceItem active = bus.ActiveAdvice().Should().ContainSingle().Subject;
        active.Actions.Should().ContainSingle().Which.Instruction.Should().Be("harvest");
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().BeNull();
        snapshots[0].Advice.Should().ContainSingle().Which.Actions.Should().ContainSingle();
        snapshots[0].StateSummaries.Should().ContainKey("Food").WhoseValue.Should().Be("Food summary");
    }

    [Fact]
    public void RemoveAppliedAction_PreservesMinisterChains()
    {
        AdviceBus bus = new();
        AdviceChainModel chain = Chain();
        bus.ReplaceMinisterAdvice("Food", [Advice("food", "Food", [Action("harvest"), Action("bill")])], "Food summary", chain);
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.RemoveAppliedAction("food", 1).Should().BeTrue();

        snapshots.Should().ContainSingle();
        snapshots[0].Chains.Should().ContainKey("Food").WhoseValue.Should().BeSameAs(chain);
    }

    [Fact]
    public void RemoveAppliedAction_RemovesAdviceWhenLastActionIsApplied()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food", "Food", [Action("bill")]));

        bool removed = bus.RemoveAppliedAction("food", 0);

        removed.Should().BeTrue();
        bus.ActiveAdvice().Should().BeEmpty();
    }

    private static AdviceItem Advice(string id, string minister) => new(
        Id: id,
        Minister: minister,
        Concern: "food_security",
        Priority: AdvicePriority.High,
        Title: "Food low",
        Body: "Body",
        Rationale: "Rationale",
        Actions: [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    private static AdviceItem Advice(string id, string minister, IReadOnlyList<AdviceAction> actions) => new(
        Id: id,
        Minister: minister,
        Concern: "food_security",
        Priority: AdvicePriority.High,
        Title: "Food low",
        Body: "Body",
        Rationale: "Rationale",
        Actions: actions,
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    private static AdviceAction Action(string instruction) =>
        new(AdviceActionKind.ProductionBill, instruction);

    private static AdviceAction CookPriorityAction() =>
        new(
            AdviceActionKind.SetPriority,
            "Put the best cook on Cook work until simple meals are stocked.",
            Owner: "Labor",
            WorkType: WorkType.Cook,
            Skill: "Cooking");

    private static AgentFlag Flag(string id, string minister) => new(
        Id: id,
        SourceMinister: minister,
        Severity: FlagSeverity.High,
        Domain: "food",
        Summary: "Food needs work",
        LaborRequests:
        [
            new LaborRequest(
                Request: "Cook work today",
                Reason: "raw food has to become meals",
                WorkType: WorkType.Cook,
                Skill: "Cooking",
                Priority: AdvicePriority.High,
                RequestedFrom: "Labor")
        ]);

    private static AdviceChainModel Chain() => new(
    [
        new AdviceChainPath("Grow path",
        [
            new AdviceChainStep("grow.trigger", "Food buffer", "4.0 days", AdviceChainStepStatus.Trigger)
        ])
    ]);

    private static string NewSnapshotRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "rimbob-advice-bus-tests",
            Guid.NewGuid().ToString("N"),
            "ministers");

    private static void CleanupSnapshot(string root)
    {
        string? directory = Path.GetDirectoryName(root);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
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
