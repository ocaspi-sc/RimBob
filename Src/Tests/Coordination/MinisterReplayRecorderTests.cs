using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Ministers;
using RimBob.LLM;
using RimBob.Tests.Food;

namespace RimBob.Tests.Coordination;

public sealed class MinisterReplayRecorderTests
{
    [Fact]
    public async Task RecordAsync_WritesSchemaV2WithFreshRawLlmMetadata()
    {
        CapturingReplayWriter writer = new();
        RawLlmOutputStore rawOutputs = new();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        DateTimeOffset captured = started.AddMilliseconds(5);
        rawOutputs.Record(new RawLlmOutputSnapshot(
            Minister: "Food",
            Provider: "Gemini",
            Model: "gemini-2.5-flash",
            ApiKeyIndex: 2,
            ApiKeyLabel: "fallback_1",
            CapturedAt: captured,
            LatencyMs: 42,
            Status: "parsed",
            ParseMode: "strict_json",
            SystemPromptChars: 10,
            UserPromptChars: 20,
            Text: "{\"advice\":[]}"));
        MinisterReplayRecorder recorder = new(writer, rawOutputs);

        await recorder.RecordAsync(new MinisterReplayEntry(
            Minister: "Food",
            Cycle: PlayCycleContext.CabinetRefresh,
            Path: "llm",
            Briefing: FoodRulesTests.Briefing(12f),
            LlmAttemptStarted: started,
            OutputKind: "advice_flags",
            Output: new { advice = Array.Empty<string>() }), CancellationToken.None);

        MinisterReplayRecord record = writer.Records.Should().ContainSingle().Subject;
        record.SchemaVersion.Should().Be(2);
        record.Trigger.Should().Be(nameof(PlayCycleTrigger.CabinetRefresh));
        record.Llm.Should().NotBeNull();
        record.Llm!.ApiKeyIndex.Should().Be(2);
        record.Llm.ApiKeyLabel.Should().Be("fallback_1");
        record.OutputKind.Should().Be("advice_flags");
    }

    [Fact]
    public async Task RecordAsync_IgnoresRawLlmMetadataOlderThanAttempt()
    {
        CapturingReplayWriter writer = new();
        RawLlmOutputStore rawOutputs = new();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        rawOutputs.Record(new RawLlmOutputSnapshot(
            Minister: "Food",
            Provider: "Gemini",
            Model: "gemini-2.5-flash",
            ApiKeyIndex: 1,
            ApiKeyLabel: "primary",
            CapturedAt: started.AddSeconds(-1),
            LatencyMs: 42,
            Status: "parsed",
            ParseMode: "strict_json",
            SystemPromptChars: 10,
            UserPromptChars: 20,
            Text: "{\"advice\":[]}"));
        MinisterReplayRecorder recorder = new(writer, rawOutputs);

        await recorder.RecordAsync(new MinisterReplayEntry(
            Minister: "Food",
            Cycle: PlayCycleContext.CabinetRefresh,
            Path: "llm_failed",
            Briefing: FoodRulesTests.Briefing(12f),
            LlmAttemptStarted: started), CancellationToken.None);

        writer.Records.Should().ContainSingle().Which.Llm.Should().BeNull();
    }

    [Fact]
    public async Task RecordAsync_UpdatesTraceStoreWithRulePath()
    {
        CapturingReplayWriter writer = new();
        MinisterTraceStore traces = new();
        MinisterReplayRecorder recorder = new(writer, traces: traces);
        RuleTraceDetails diagnostics = new(
            SelectedRule: "emergency_food_flag",
            MatchedSignals:
            [
                new RuleTraceEntry(
                    Rule: "emergency_food_flag",
                    Outcome: "selected",
                    Reason: "food buffer 4.0d is below the 7d emergency threshold")
            ],
            SuppressedCandidates: []);

        await recorder.RecordAsync(new MinisterReplayEntry(
            Minister: "Food",
            Cycle: PlayCycleContext.ManualTrigger,
            Path: "rules",
            Briefing: FoodRulesTests.Briefing(4f),
            RuleTrace: "emergency_food_flag",
            RuleDiagnostics: diagnostics), CancellationToken.None);

        traces.Latest("Food").Should().NotBeNull();
        MinisterTraceSnapshot snapshot = traces.Latest("Food")!;
        snapshot.Status.Should().Be("completed");
        snapshot.Trigger.Should().Be(nameof(PlayCycleTrigger.ManualTrigger));
        snapshot.Path.Should().Be("rules");
        snapshot.RuleFired.Should().Be("emergency_food_flag");
        snapshot.RuleDiagnostics.Should().NotBeNull();
        snapshot.RuleDiagnostics!.SelectedRule.Should().Be("emergency_food_flag");
        snapshot.Note.Should().Contain("emergency_food_flag");
    }

    [Fact]
    public async Task RecordAsync_UpdatesRunningTraceWithLlmFailure()
    {
        CapturingReplayWriter writer = new();
        MinisterTraceStore traces = new();
        traces.Begin("Food", PlayCycleContext.CabinetRefresh);
        MinisterReplayRecorder recorder = new(writer, traces: traces);

        await recorder.RecordAsync(new MinisterReplayEntry(
            Minister: "Food",
            Cycle: PlayCycleContext.CabinetRefresh,
            Path: "llm_failed",
            Briefing: FoodRulesTests.Briefing(12f),
            EscalationReason: "bootstrap_first_live_cycle",
            Error: new ReplayErrorSummary(nameof(InvalidOperationException), "No Gemini API keys configured.")),
            CancellationToken.None);

        traces.Latest("Food").Should().NotBeNull();
        MinisterTraceSnapshot running = traces.Latest("Food")!;
        running.Status.Should().Be("running");
        running.Path.Should().Be("llm_failed");
        running.EscalationReason.Should().Be("bootstrap_first_live_cycle");
        running.ErrorType.Should().Be(nameof(InvalidOperationException));
        running.ErrorMessage.Should().Be("No Gemini API keys configured.");

        traces.Complete("Food");
        traces.Latest("Food").Should().NotBeNull();
        MinisterTraceSnapshot completed = traces.Latest("Food")!;
        completed.Status.Should().Be("completed");
        completed.Path.Should().Be("llm_failed");
        completed.ErrorMessage.Should().Be("No Gemini API keys configured.");
    }

    private sealed class CapturingReplayWriter : IReplayCorpusWriter
    {
        public List<MinisterReplayRecord> Records { get; } = [];

        public Task WriteAsync(MinisterReplayRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
