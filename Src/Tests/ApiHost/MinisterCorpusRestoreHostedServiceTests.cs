using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;
using RimBob.Host;

namespace RimBob.Tests.ApiHost;

public sealed class MinisterCorpusRestoreHostedServiceTests
{
    [Fact]
    public async Task StartAsync_RestoresRequestFlagsWithRestampedTtl()
    {
        string directory = NewTempRoot();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(1);
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            AgentFlag expiredFlag = BuildingRequestFlag(
                "chef:freezer",
                expiresAt: DateTimeOffset.UtcNow.AddHours(-2));
            await writer.WriteAsync(
                Record("Chef", now.AddDays(-2), [], [expiredFlag]),
                CancellationToken.None);
            FlagChannel flags = new();
            AdviceBus advice = new();
            CorpusRestoreStatusStore status = new();
            MinisterCorpusRestoreHostedService service = Service(directory, flags, advice, status, now);

            await service.StartAsync(CancellationToken.None);

            AgentFlag restored = flags.Active().Should().ContainSingle().Subject;
            restored.Id.Should().Be("chef:freezer");
            restored.ExpiresAt.Should().Be(now.AddHours(24));
            restored.BuildingRequests.Should().ContainSingle()
                .Which.RequestedFrom.Should().Be("Willie");
            CorpusRestoreMinisterStatus provenance = status.Snapshot().Ministers.Should()
                .ContainSingle(s => s.Minister == "Chef")
                .Subject;
            provenance.Flags.RestoredFromCorpus.Should().BeTrue();
            provenance.Flags.CapturedAt.Should().Be(now.AddDays(-2));
            provenance.Flags.RestoredCount.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    [Fact]
    public async Task StartAsync_SkipsFlagRestoreWhenLiveRequestAlreadyExists()
    {
        string directory = NewTempRoot();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(1);
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            await writer.WriteAsync(
                Record("Chef", now.AddDays(-1), [], [BuildingRequestFlag("chef:corpus", DateTimeOffset.UtcNow.AddHours(-1))]),
                CancellationToken.None);
            FlagChannel flags = new();
            flags.Publish(BuildingRequestFlag("chef:live", DateTimeOffset.UtcNow.AddHours(1)));
            AdviceBus advice = new();
            CorpusRestoreStatusStore status = new();
            MinisterCorpusRestoreHostedService service = Service(directory, flags, advice, status, now);

            await service.StartAsync(CancellationToken.None);

            flags.Active().Should().ContainSingle()
                .Which.Id.Should().Be("chef:live");
            status.Snapshot().Ministers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    [Fact]
    public async Task StartAsync_RestoresAdviceWithOptions()
    {
        string directory = NewTempRoot();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(1);
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            await writer.WriteAsync(
                Record("Willie", now.AddDays(-1), [Advice("willie_corpus", [Option("option-a")])], []),
                CancellationToken.None);
            FlagChannel flags = new();
            AdviceBus advice = new();
            CorpusRestoreStatusStore status = new();
            MinisterCorpusRestoreHostedService service = Service(directory, flags, advice, status, now);

            await service.StartAsync(CancellationToken.None);

            AdviceItem restored = advice.ActiveAdvice().Should().ContainSingle().Subject;
            restored.Id.Should().Be("willie_corpus");
            restored.Options.Should().ContainSingle().Which.Id.Should().Be("option-a");
            CorpusRestoreMinisterStatus provenance = status.Snapshot().Ministers.Should()
                .ContainSingle(s => s.Minister == "Willie")
                .Subject;
            provenance.Advice.RestoredFromCorpus.Should().BeTrue();
            provenance.Advice.CapturedAt.Should().Be(now.AddDays(-1));
            provenance.Advice.RestoredCount.Should().Be(1);
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    [Fact]
    public async Task StartAsync_SkipsAdviceRestoreWhenOptionsAlreadyExist()
    {
        string directory = NewTempRoot();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(1);
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            await writer.WriteAsync(
                Record("Willie", now.AddDays(-1), [Advice("willie_corpus", [Option("corpus")])], []),
                CancellationToken.None);
            FlagChannel flags = new();
            AdviceBus advice = new();
            advice.ReplaceMinisterAdvice("Willie", [Advice("willie_live", [Option("live")])]);
            CorpusRestoreStatusStore status = new();
            MinisterCorpusRestoreHostedService service = Service(directory, flags, advice, status, now);

            await service.StartAsync(CancellationToken.None);

            AdviceItem live = advice.ActiveAdvice().Should().ContainSingle().Subject;
            live.Id.Should().Be("willie_live");
            live.Options.Should().ContainSingle().Which.Id.Should().Be("live");
            status.Snapshot().Ministers.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    private static MinisterCorpusRestoreHostedService Service(
        string directory,
        FlagChannel flags,
        AdviceBus advice,
        CorpusRestoreStatusStore status,
        DateTimeOffset now) =>
        new(
            new ReplayCorpusOutputReader(directory),
            flags,
            advice,
            new MinisterRegistry(),
            status,
            new FixedTimeProvider(now),
            NullLogger<MinisterCorpusRestoreHostedService>.Instance);

    private static MinisterReplayRecord Record(
        string minister,
        DateTimeOffset capturedAt,
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> flags) =>
        new(
            SchemaVersion: 2,
            CapturedAt: capturedAt,
            Minister: minister,
            Trigger: PlayCycleTrigger.CabinetRefresh.ToString(),
            WakeupPayload: null,
            Flag: null,
            Path: "rules",
            Briefing: new { test = true },
            Context: null,
            RuleTrace: null,
            RuleTraceDetails: null,
            EscalationReason: null,
            EscalationContext: null,
            GuideCitations: null,
            Advice: advice,
            Flags: flags,
            StateSummary: $"{minister} state");

    private static AgentFlag BuildingRequestFlag(string id, DateTimeOffset expiresAt) => new(
        Id: id,
        SourceMinister: "Chef",
        Severity: FlagSeverity.High,
        Domain: "food",
        Summary: "Freezer needed",
        BuildingRequests:
        [
            new BuildingRequest(
                Request: "Build a freezer",
                Reason: "Meals will spoil without cold storage.",
                TargetClass: BuildingClass.Freezer,
                RoomClass: RoomClass.Freezer,
                RequestedFrom: "Willie")
        ],
        ExpiresAt: expiresAt);

    private static AdviceItem Advice(string id, IReadOnlyList<AdviceOption> options) => new(
        Id: id,
        Minister: "Willie",
        Priority: AdvicePriority.High,
        Title: "Build a freezer",
        Body: "Body",
        Rationale: "Rationale",
        Actions: [],
        GuideCitationIds: [],
        IssuedAt: DateTimeOffset.UtcNow,
        ExpiresAt: DateTimeOffset.UtcNow.AddHours(4),
        Options: options);

    private static AdviceOption Option(string id) => new(
        Id: id,
        Label: "Freezer option",
        Summary: "Near kitchen",
        BlueprintGroup: new BlueprintGroup(
            Label: "Freezer option",
            MapId: 1,
            Assets:
            [
                new BlueprintAsset(
                    Role: "wall",
                    DefName: "Wall",
                    StuffDefName: "WoodLog",
                    Cell: new MapCell(10, 20),
                    Rotation: 0)
            ]),
        EstimatedMaterials: []);

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "rimbob-corpus-restore-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
