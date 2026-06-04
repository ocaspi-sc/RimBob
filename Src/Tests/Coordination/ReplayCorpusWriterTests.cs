using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Tests.Food;

namespace RimBob.Tests.Coordination;

public sealed class ReplayCorpusWriterTests
{
    [Fact]
    public void RuleEvaluationTrace_DeserializesMissingEmissionsAsEmpty()
    {
        JsonSerializerOptions json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        RuleEvaluationTrace trace = JsonSerializer.Deserialize<RuleEvaluationTrace>(
            """
            {
              "rule": "emergency_food_flag",
              "outcome": "selected",
              "conditions": "food buffer low",
              "output_action": "mark_hunt",
              "reason": "food buffer low"
            }
            """,
            json) ?? throw new InvalidOperationException("Trace row should deserialize.");

        trace.Emissions.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_AppendsSnakeCaseJsonlUnderMinisterDatePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rimbob-replay-tests", Guid.NewGuid().ToString("N"));
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
                Priority: Priority.High,
                Title: "Food test",
                Body: "Body",
                Rationale: "Rationale",
                Actions:
                [
                    new AdviceAction(
                        AdviceActionKind.MarkHunt,
                        "Mark up to 2 hares for hunting.",
                        Apply: new MarkHuntAreaApply(
                            "Mark hunt",
                            "2 hare hunt targets",
                            1,
                            new MapRect(40, 50, 41, 50),
                            ["hare-1", "hare-2"],
                            2))
                ],
                GuideCitationIds: [],
                Stamp: new AdviceStamp(capturedAt, capturedAt.AddHours(4)));
            AdviceChainModel chain = new(
            [
                new AdviceChainPath("Grow path",
                [
                    new AdviceChainStep("grow.trigger", "Food buffer", "4.0 days", AdviceChainStepStatus.Trigger)
                ])
            ]);
            RuleTraceDetails traceDetails = new(
                SelectedRule: "emergency_food_flag",
                AllRules:
                [
                    new RuleEvaluationTrace(
                        Rule: "emergency_food_flag",
                        Outcome: RuleOutcome.Selected,
                        Conditions: "food buffer 4.0d is below the 7d emergency threshold",
                        OutputAction: "mark_hunt",
                        Reason: "food buffer 4.0d is below the 7d emergency threshold",
                        Emissions:
                        [
                            new RuleEmission("advise", Priority.High, "Food test", null, null, null)
                        ])
                ]);

            MinisterReplayRecord record = new(
                SchemaVersion: 2,
                CapturedAt: capturedAt,
                Minister: "Food",
                Trigger: PlayCycleTrigger.CabinetRefresh.ToString(),
                WakeupPayload: "dashboard",
                Flag: null,
                Path: "rules",
                Briefing: briefing,
                Context: MinisterBriefingContext.Empty,
                RuleTrace: "emergency_food_flag",
                RuleTraceDetails: traceDetails,
                EscalationReason: null,
                EscalationContext: null,
                GuideCitations: [citation],
                Advice: [advice],
                Flags: [],
                StateSummary: "Food is low and needs action.",
                Chain: chain,
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
                    RawOutput: "{\"advice\":[]}",
                    ApiKeyIndex: 2,
                    ApiKeyLabel: "fallback_1"),
                OutputKind: "advice_flags",
                Output: new { advice = new[] { advice.Id }, flags = Array.Empty<string>() });

            await writer.WriteAsync(record, CancellationToken.None);
            await writer.WriteAsync(record, CancellationToken.None);

            string path = Path.Combine(directory, "food-20260514.jsonl");
            string[] lines = await File.ReadAllLinesAsync(path);
            lines.Should().HaveCount(2);

            using JsonDocument document = JsonDocument.Parse(lines[0]);
            JsonElement root = document.RootElement;
            root.GetProperty("schema_version").GetInt32().Should().Be(2);
            root.GetProperty("captured_at").GetString().Should().Contain("2026-05-14");
            root.GetProperty("minister").GetString().Should().Be("Food");
            root.GetProperty("trigger").GetString().Should().Be("CabinetRefresh");
            root.GetProperty("wakeup_payload").GetString().Should().Be("dashboard");
            root.GetProperty("path").GetString().Should().Be("rules");
            root.GetProperty("briefing").GetProperty("estimated_days_of_food").GetSingle().Should().Be(4f);
            root.GetProperty("context").GetProperty("short_term_domains").GetArrayLength().Should().Be(0);
            root.GetProperty("rule_trace").GetString().Should().Be("emergency_food_flag");
            root.GetProperty("rule_trace_details").GetProperty("selected_rule").GetString().Should().Be("emergency_food_flag");
            JsonElement rule = root.GetProperty("rule_trace_details").GetProperty("all_rules")[0];
            rule.GetProperty("rule").GetString().Should().Be("emergency_food_flag");
            rule.GetProperty("outcome").GetString().Should().Be("selected");
            rule.GetProperty("output_action").GetString().Should().Be("mark_hunt");
            JsonElement emission = rule.GetProperty("emissions")[0];
            emission.GetProperty("kind").GetString().Should().Be("advise");
            emission.GetProperty("priority").GetString().Should().Be("high");
            emission.GetProperty("label").GetString().Should().Be("Food test");
            root.GetProperty("guide_citations").GetArrayLength().Should().Be(1);
            root.GetProperty("guide_citations")[0].GetProperty("cite_id").GetString().Should().Be("food-guide-1");
            root.GetProperty("state_summary").GetString().Should().Be("Food is low and needs action.");
            root.GetProperty("chain").GetProperty("paths")[0].GetProperty("steps")[0].GetProperty("status").GetString().Should().Be("trigger");
            root.GetProperty("llm").GetProperty("raw_output").GetString().Should().Be("{\"advice\":[]}");
            root.GetProperty("llm").GetProperty("api_key_index").GetInt32().Should().Be(2);
            root.GetProperty("llm").GetProperty("api_key_label").GetString().Should().Be("fallback_1");
            root.GetProperty("output_kind").GetString().Should().Be("advice_flags");
            root.GetProperty("output").GetProperty("advice").GetArrayLength().Should().Be(1);
            root.GetProperty("advice").GetArrayLength().Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
