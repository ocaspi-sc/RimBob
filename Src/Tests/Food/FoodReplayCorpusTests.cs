using System.Text.Json;
using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Food;

namespace RimBob.Tests.Food;

public sealed class FoodReplayCorpusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void HistoricalRuleReplayCorpus_RerunsWithoutLlmAndMatchesStableOutput()
    {
        string path = FindFixture("food-rules-history.jsonl");
        IReadOnlyList<FoodRuleReplayCase> cases = ReadRuleCases(path);

        cases.Should().NotBeEmpty();
        foreach (FoodRuleReplayCase replayCase in cases)
        {
            RulesResult result = new Rules().Evaluate(replayCase.Briefing, ColonyContext.Default);

            Decision decision = result.Should()
                .BeOfType<Decision>($"{replayCase.SourceId} is a saved rules-path replay record")
                .Subject;

            decision.Trace.Should().Be(replayCase.RuleTrace, replayCase.SourceId);
            AssertAdviceMatches(replayCase.SourceId, replayCase.ExpectedAdvice, decision.Advice);
            AssertFlagsMatch(replayCase.SourceId, replayCase.ExpectedFlags, decision.Flags);
        }
    }

    private static IReadOnlyList<FoodRuleReplayCase> ReadRuleCases(string path)
    {
        List<FoodRuleReplayCase> cases = [];
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string sourceId = root.TryGetProperty("source_id", out JsonElement source)
                ? source.GetString() ?? $"{path}:{lineNumber}"
                : $"{path}:{lineNumber}";

            root.GetProperty("minister").GetString().Should().Be("Food", sourceId);
            root.GetProperty("path").GetString().Should().Be("rules", sourceId);

            cases.Add(new FoodRuleReplayCase(
                SourceId: sourceId,
                RuleTrace: RequiredString(root, "rule_trace", sourceId),
                Briefing: Required<FoodBriefing>(root, "briefing", sourceId),
                ExpectedAdvice: Required<IReadOnlyList<AdviceItem>>(root, "advice", sourceId),
                ExpectedFlags: Required<IReadOnlyList<AgentFlag>>(root, "flags", sourceId)));
        }

        return cases;
    }

    private static void AssertAdviceMatches(
        string sourceId,
        IReadOnlyList<AdviceItem> expected,
        IReadOnlyList<AdviceItem> actual)
    {
        actual.Should().HaveCount(expected.Count, sourceId);
        for (int i = 0; i < expected.Count; i++)
        {
            AdviceItem expectedItem = expected[i];
            AdviceItem actualItem = actual[i];
            actualItem.Id.Should().Be(expectedItem.Id, sourceId);
            actualItem.Minister.Should().Be(expectedItem.Minister, sourceId);
            actualItem.AdviceType.Should().Be(expectedItem.AdviceType, sourceId);
            actualItem.Priority.Should().Be(expectedItem.Priority, sourceId);
            actualItem.Title.Should().Be(expectedItem.Title, sourceId);
            actualItem.Body.Should().Be(expectedItem.Body, sourceId);
            actualItem.Rationale.Should().Be(expectedItem.Rationale, sourceId);
            actualItem.Steps.Should().Equal(expectedItem.Steps);
            actualItem.GuideCitationIds.Should().Equal(expectedItem.GuideCitationIds);
            actualItem.IssuedInGameTick.Should().Be(expectedItem.IssuedInGameTick, sourceId);
            actualItem.BriefingRef.Should().Be(expectedItem.BriefingRef, sourceId);
            actualItem.Supersedes.Should().Be(expectedItem.Supersedes, sourceId);
            actualItem.AutonomyAtIssue.Should().Be(expectedItem.AutonomyAtIssue, sourceId);
        }
    }

    private static void AssertFlagsMatch(
        string sourceId,
        IReadOnlyList<AgentFlag> expected,
        IReadOnlyList<AgentFlag> actual)
    {
        actual.Should().HaveCount(expected.Count, sourceId);
        for (int i = 0; i < expected.Count; i++)
        {
            AgentFlag expectedFlag = expected[i];
            AgentFlag actualFlag = actual[i];
            actualFlag.Id.Should().Be(expectedFlag.Id, sourceId);
            actualFlag.SourceMinister.Should().Be(expectedFlag.SourceMinister, sourceId);
            actualFlag.Severity.Should().Be(expectedFlag.Severity, sourceId);
            actualFlag.Domain.Should().Be(expectedFlag.Domain, sourceId);
            actualFlag.Summary.Should().Be(expectedFlag.Summary, sourceId);
            actualFlag.Detail.Should().Be(expectedFlag.Detail, sourceId);
            (actualFlag.Requests ?? []).Should().Equal(expectedFlag.Requests ?? []);
        }
    }

    private static T Required<T>(JsonElement root, string propertyName, string sourceId)
    {
        root.TryGetProperty(propertyName, out JsonElement property)
            .Should().BeTrue($"{sourceId} must include {propertyName}");

        T? value = property.Deserialize<T>(JsonOptions);
        value.Should().NotBeNull($"{sourceId} must deserialize {propertyName}");
        return value!;
    }

    private static string RequiredString(JsonElement root, string propertyName, string sourceId)
    {
        root.TryGetProperty(propertyName, out JsonElement property)
            .Should().BeTrue($"{sourceId} must include {propertyName}");
        string? value = property.GetString();
        value.Should().NotBeNullOrWhiteSpace($"{sourceId} must include {propertyName}");
        return value!;
    }

    private static string FindFixture(string fileName)
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "Src",
                "Tests",
                "Food",
                "Fixtures",
                "replay-corpus",
                fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find Food replay fixture {fileName}.");
    }

    private sealed record FoodRuleReplayCase(
        string SourceId,
        string RuleTrace,
        FoodBriefing Briefing,
        IReadOnlyList<AdviceItem> ExpectedAdvice,
        IReadOnlyList<AgentFlag> ExpectedFlags);
}
