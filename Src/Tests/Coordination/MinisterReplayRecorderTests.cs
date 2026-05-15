using FluentAssertions;
using RimAI.Coordination;
using RimAI.Core.Ministers;
using RimAI.LLM;
using RimAI.Tests.Food;

namespace RimAI.Tests.Coordination;

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
