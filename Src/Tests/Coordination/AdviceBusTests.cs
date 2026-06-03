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
            Version: 1, UpdatedGameDate: TestDate(),
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
        Action act = () => bus.Publish(new AgendaUpdated(InputBuilder.Default.ToAgenda(7, TestDate())));
        act.Should().NotThrow();
    }

    [Fact]
    public void PublishAdvice_RetainsActiveAdviceForReplay()
    {
        AdviceBus bus = new();
        AdviceItem item = Advice("a1", "Chef");

        bus.Publish(item);

        bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("a1");
    }

    [Fact]
    public void ReplaceMinisterAdvice_ReplacesOnlyThatMinistersActiveSet()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("old_food", "Chef"));
        bus.Publish(Advice("old_defense", "Defense"));
        List<AdviceSnapshot> snapshots = [];
        List<AdviceItem> published = [];
        bus.AdviceSnapshotPublished += snapshots.Add;
        bus.AdvicePublished += published.Add;

        bus.ReplaceMinisterAdvice("Chef", [Advice("new_food", "Chef")], "Food is low but actionable.");

        bus.ActiveAdvice().Select(a => a.Id).Should().BeEquivalentTo(["new_food", "old_defense"]);
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().Be("Chef");
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

        bus.ReplaceMinisterAdvice("Chef", [Advice("new_food", "Chef")], "Food is low.", chain);

        snapshots.Should().ContainSingle();
        snapshots[0].Chain.Should().BeSameAs(chain);
        AdviceSnapshot active = bus.ActiveSnapshot();
        active.Chains.Should().ContainKey("Chef").WhoseValue.Should().BeSameAs(chain);
    }

    [Fact]
    public void ReplaceMinisterAdvice_PublishesAndReplaysMinisterFlags()
    {
        AdviceBus bus = new();
        AgentFlag flag = Flag("food:emergency", "Chef");
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.ReplaceMinisterAdvice("Chef", [Advice("new_food", "Chef")], "Food is low.", flags: [flag]);

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

            bus.ReplaceMinisterAdvice("Chef", [Advice("old_food", "Chef")], "Old summary.");
            bus.ReplaceMinisterAdvice("Chef", [Advice("new_food", "Chef")], "New summary.");
            await store.FlushPendingAdviceAsync();

            MinisterOutputStore restored = await MinisterOutputStore.LoadAsync(root);
            AdviceSnapshot? snapshot = restored.GetAdviceSnapshot("chef");

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
        bus.Publish(Advice("old_food", "Chef"));
        bus.Publish(Advice("old_defense", "Defense"));
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.ReplaceMinisterAdvice("Chef", []);

        bus.ActiveAdvice().Should().ContainSingle().Which.Id.Should().Be("old_defense");
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().Be("Chef");
        snapshots[0].Advice.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceMinisterAdvice_AcceptsFoodScopeKeyForChef()
    {
        AdviceBus bus = new();

        bus.ReplaceMinisterAdvice("food", [Advice("new_food", "Chef")], "Food is stable.");

        bus.ActiveAdvice().Should().ContainSingle().Which.Minister.Should().Be("Chef");
        bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Chef").WhoseValue.Should().Be("Food is stable.");
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
        bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Chef").WhoseValue.Should().Be("Stored Food state.");
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

        bus.ReplaceMinisterAdvice("Chef", [], "Food is stable.");

        AdviceSnapshot snapshot = bus.ActiveSnapshot();

        snapshot.Minister.Should().BeNull();
        snapshot.Advice.Should().BeEmpty();
        snapshot.StateSummaries.Should().ContainKey("Chef").WhoseValue.Should().Be("Food is stable.");
    }

    [Fact]
    public void MarkActionApplied_ClearsHandleAndPublishesAppliedSnapshot()
    {
        AdviceBus bus = new();
        bus.ReplaceMinisterAdvice("Chef", [Advice("food", "Chef", [Action("harvest"), Action("bill", executable: true)])], "Food summary");
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;
        AdviceActionApplyResult result = ApplyResult("applied");

        bool marked = bus.MarkActionApplied("food", 1, result);

        marked.Should().BeTrue();
        AdviceItem active = bus.ActiveAdvice().Should().ContainSingle().Subject;
        active.Actions.Should().HaveCount(2);
        active.Actions[0].ApplyResult.Should().BeNull();
        active.Actions[1].Instruction.Should().Be("bill");
        active.Actions[1].Apply.Should().BeNull();
        active.Actions[1].ApplyResult.Should().Be(result);
        snapshots.Should().ContainSingle();
        snapshots[0].Minister.Should().BeNull();
        snapshots[0].Advice.Should().ContainSingle().Which.Actions.Should().HaveCount(2);
        snapshots[0].Advice[0].Actions[1].ApplyResult.Should().Be(result);
        snapshots[0].StateSummaries.Should().ContainKey("Chef").WhoseValue.Should().Be("Food summary");
    }

    [Fact]
    public void MarkActionApplied_PreservesMinisterChains()
    {
        AdviceBus bus = new();
        AdviceChainModel chain = Chain();
        bus.ReplaceMinisterAdvice("Chef", [Advice("food", "Chef", [Action("harvest"), Action("bill")])], "Food summary", chain);
        List<AdviceSnapshot> snapshots = [];
        bus.AdviceSnapshotPublished += snapshots.Add;

        bus.MarkActionApplied("food", 1, ApplyResult("applied")).Should().BeTrue();

        snapshots.Should().ContainSingle();
        snapshots[0].Chains.Should().ContainKey("Chef").WhoseValue.Should().BeSameAs(chain);
    }

    [Fact]
    public void MarkActionApplied_PreservesAdviceWhenLastActionIsApplied()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food", "Chef", [Action("bill", executable: true)]));

        bool marked = bus.MarkActionApplied("food", 0, ApplyResult("already_satisfied"));

        marked.Should().BeTrue();
        AdviceAction action = bus.ActiveAdvice().Should().ContainSingle()
            .Which.Actions.Should().ContainSingle()
            .Subject;
        action.Instruction.Should().Be("bill");
        action.Apply.Should().BeNull();
        action.ApplyResult.Should().NotBeNull();
        action.ApplyResult!.Status.Should().Be("already_satisfied");
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

    private static AdviceItem Advice(string id, string minister, IReadOnlyList<AdviceAction> actions) => new(
        Id: id,
        Minister: minister,
        Priority: AdvicePriority.High,
        Title: "Food low",
        Body: "Body",
        Rationale: "Rationale",
        Actions: actions,
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));

    private static AdviceAction Action(string instruction, bool executable = false) =>
        new(
            AdviceActionKind.ProductionBill,
            instruction,
            Apply: executable
                ? new UpsertProductionBillApply(
                    "Set simple meal bill",
                    "simple meal bill",
                    MapId: 1,
                    WorkbenchBuildingId: "10",
                    RecipeSelectorKey: "simple_meal",
                    RepeatMode: "TargetCount",
                    TargetCount: 12)
                : null);

    private static AdviceActionApplyResult ApplyResult(string status) =>
        new(status, status == "applied" ? "Applied." : "Already satisfied.", AdviceApplyKind.UpsertProductionBill, DateTimeOffset.UnixEpoch);

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

    private static GameDate TestDate() =>
        GameTime.Create("1st of Aprimay, 5500, 0h", 0, 5500, "Aprimay", 1, 0);

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
    public static MayorAgenda ToAgenda(this MayorAgendaInput input, int version, GameDate date) => new(
        Version: version, UpdatedGameDate: date,
        GeneratedAt: DateTimeOffset.UnixEpoch,
        Posture: input.Posture, StateOfTheUnion: input.StateOfTheUnion,
        UpdateNotes: input.UpdateNotes, ShortTerm: input.ShortTerm, LongTerm: input.LongTerm,
        CabinetDirection: input.CabinetDirection,
        GuideCitations: input.GuideCitations ?? []);

    private static GameDate TestDate() =>
        GameTime.Create("1st of Aprimay, 5500, 0h", 0, 5500, "Aprimay", 1, 0);
}
