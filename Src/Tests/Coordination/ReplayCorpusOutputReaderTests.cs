using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Tests.Coordination;

public sealed class ReplayCorpusOutputReaderTests
{
    [Fact]
    public async Task Latest_ReturnsNewestRecordMatchingPredicate()
    {
        string directory = NewTempRoot();
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            DateTimeOffset oldCapture = new(2026, 5, 16, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset newCapture = new(2026, 5, 17, 10, 0, 0, TimeSpan.Zero);
            await writer.WriteAsync(
                Record("Willie", oldCapture, [Advice("old_options", [Option("old")])], []),
                CancellationToken.None);
            await writer.WriteAsync(
                Record("Willie", newCapture, [Advice("new_options", [Option("new")])], []),
                CancellationToken.None);
            await writer.WriteAsync(
                Record("Willie", newCapture.AddMinutes(1), [Advice("newer_without_options")], []),
                CancellationToken.None);

            File.SetLastWriteTimeUtc(Path.Combine(directory, "willie-20260516.jsonl"), oldCapture.UtcDateTime);
            File.SetLastWriteTimeUtc(Path.Combine(directory, "willie-20260517.jsonl"), newCapture.UtcDateTime);
            ReplayCorpusOutputReader reader = new(directory);

            MinisterReplayRecord? record = await reader.LatestAsync("Willie", HasAdviceOptions);

            record.Should().NotBeNull();
            record!.Advice.Should().ContainSingle().Which.Id.Should().Be("new_options");
            record.Advice[0].Options.Should().ContainSingle().Which.Id.Should().Be("new");
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    [Fact]
    public async Task LatestAsync_ReturnsNullForMissingOrEmptyDirectory()
    {
        string directory = NewTempRoot();
        ReplayCorpusOutputReader missingReader = new(directory);

        (await missingReader.LatestAsync("Willie")).Should().BeNull();

        try
        {
            Directory.CreateDirectory(directory);
            ReplayCorpusOutputReader emptyReader = new(directory);

            (await emptyReader.LatestAsync("Willie")).Should().BeNull();
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    private static bool HasAdviceOptions(MinisterReplayRecord record) =>
        record.Advice.Any(advice => advice.Options is { Count: > 0 });

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

    private static AdviceItem Advice(string id, IReadOnlyList<AdviceOption>? options = null) => new(
        Id: id,
        Minister: "Willie",
        Concern: "functional_rooms",
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
        Path.Combine(Path.GetTempPath(), "rimbob-replay-output-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
