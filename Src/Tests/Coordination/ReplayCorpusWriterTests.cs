using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Tests.Food;

namespace RimAI.Tests.Coordination;

public sealed class ReplayCorpusWriterTests
{
    [Fact]
    public async Task WriteAsync_AppendsSnakeCaseJsonlUnderMinisterDatePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rimai-replay-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ReplayCorpusWriter writer = new(directory, NullLogger<ReplayCorpusWriter>.Instance);
            DateTimeOffset capturedAt = new(2026, 5, 14, 10, 20, 30, TimeSpan.Zero);
            FoodBriefing briefing = FoodRulesTests.Briefing(4f);
            GuideCitation citation = new(
                CiteId: "food-guide-1",
                SourcePath: "Docs/guides/food.md",
                Heading: "Emergency food",
                Snippet: "Keep a short-term food buffer.");
            AdviceItem advice = new(
                Id: "food_test",
                Minister: "Food",
                AdviceType: "food_security",
                Priority: AdvicePriority.High,
                Title: "Food test",
                Body: "Body",
                Rationale: "Rationale",
                ResourceRequests: [],
                SuggestedActions: [],
                GuideCitationIds: [],
                IssuedAt: capturedAt,
                ExpiresAt: capturedAt.AddHours(4));

            MinisterReplayRecord record = new(
                SchemaVersion: 1,
                CapturedAt: capturedAt,
                Minister: "Food",
                Trigger: PlayCycleTrigger.CabinetRefresh.ToString(),
                WakeupPayload: "dashboard",
                Flag: null,
                Path: "rules",
                Briefing: briefing,
                Context: MinisterBriefingContext.Empty,
                RuleTrace: "emergency_food_flag",
                EscalationReason: null,
                EscalationContext: null,
                GuideCitations: [citation],
                Advice: [advice],
                Flags: [],
                Error: null,
                Llm: new ReplayLlmMetadata(
                    Provider: "Gemini",
                    Model: "gemini-2.5-flash",
                    CapturedAt: capturedAt,
                    SystemPromptChars: 10,
                    UserPromptChars: 20,
                    Status: "parsed",
                    ParseMode: "strict_json",
                    LatencyMs: 30,
                    RawOutput: "{\"advice\":[]}"));

            await writer.WriteAsync(record, CancellationToken.None);
            await writer.WriteAsync(record, CancellationToken.None);

            string path = Path.Combine(directory, "food-20260514.jsonl");
            string[] lines = await File.ReadAllLinesAsync(path);
            lines.Should().HaveCount(2);

            using JsonDocument document = JsonDocument.Parse(lines[0]);
            JsonElement root = document.RootElement;
            root.GetProperty("schema_version").GetInt32().Should().Be(1);
            root.GetProperty("captured_at").GetString().Should().Contain("2026-05-14");
            root.GetProperty("minister").GetString().Should().Be("Food");
            root.GetProperty("trigger").GetString().Should().Be("CabinetRefresh");
            root.GetProperty("wakeup_payload").GetString().Should().Be("dashboard");
            root.GetProperty("path").GetString().Should().Be("rules");
            root.GetProperty("briefing").GetProperty("estimated_days_of_food").GetSingle().Should().Be(4f);
            root.GetProperty("context").GetProperty("short_term_domains").GetArrayLength().Should().Be(0);
            root.GetProperty("rule_trace").GetString().Should().Be("emergency_food_flag");
            root.GetProperty("guide_citations").GetArrayLength().Should().Be(1);
            root.GetProperty("guide_citations")[0].GetProperty("cite_id").GetString().Should().Be("food-guide-1");
            root.GetProperty("llm").GetProperty("raw_output").GetString().Should().Be("{\"advice\":[]}");
            root.GetProperty("advice").GetArrayLength().Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
