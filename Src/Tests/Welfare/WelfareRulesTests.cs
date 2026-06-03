using System.Text.Json;
using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Ministers.Welfare;

namespace RimBob.Tests.Welfare;

public sealed class WelfareRulesTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void NewColonyWithoutBeds_EmitsShelterFloorAdviceAndWillieRequest()
    {
        Decision decision = Evaluate("new-colony");

        decision.Trace.Should().Be("shelter_floor");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_shelter_floor");
        advice.Minister.Should().Be("Welfare");
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.Body.Should().Contain("All 3 colonists");
        advice.Rationale.Should().Contain("slept on ground");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:shelter_floor");
        flag.SourceMinister.Should().Be("Welfare");
        flag.Severity.Should().Be(FlagSeverity.High);
        BuildingRequest request = flag.BuildingRequests.Should().ContainSingle().Subject;
        request.Request.Should().Be("add 3 beds in a roofed barracks");
        request.TargetClass.Should().Be(BuildingClass.Bed);
        request.TargetDef.Should().Be("Bed");
        request.RoomClass.Should().Be(RoomClass.Barracks);
        request.CapacityNeed.Should().Be(new CapacityNeed(CapacityMeasure.Beds, 3));
        request.RequestedFrom.Should().Be("Willie");
        request.Adjacency.Should().BeNull();

        decision.Diagnostics.Should().NotBeNull();
        AssertAllRulesIncludeTableAndStableFallback(decision);
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "shelter_floor" &&
            row.Outcome == "selected");
        decision.Diagnostics.EmittedFlags.Should().ContainSingle(row =>
            row.FlagId == "welfare:shelter_floor" &&
            row.RequestCount == 1);
    }

    [Fact]
    public void HasBedsAndNoBreakRisk_ReturnsEmptyStableDecision()
    {
        Decision decision = Evaluate("has-beds");

        decision.Trace.Should().Be("needs_stable");
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Diagnostics.Should().NotBeNull();
        AssertAllRulesIncludeTableAndStableFallback(decision);
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "needs_stable" &&
            row.Outcome == "selected");
    }

    [Fact]
    public void BreakRisk_EmitsMoodAdviceWithoutBuildFlag()
    {
        Decision decision = Evaluate("break-risk");

        decision.Trace.Should().Be("break_risk");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_break_risk");
        advice.Priority.Should().Be(AdvicePriority.High);
        advice.Body.Should().Contain("Alice");
        advice.Body.Should().Contain("slept on ground");
        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:break_risk_willie");
        flag.Attention.Should().ContainSingle()
            .Which.RequestedFrom.Should().Be("Willie");
        advice.Actions.Should().ContainSingle()
            .Which.Instruction.Should().Contain("slept on ground");
    }

    [Fact]
    public void BreakRiskAtHalfTheColony_IsCritical()
    {
        WelfareSourceBriefing briefing = LoadFixture("break-risk") with
        {
            ColonistCount = 2,
            Mood = new WelfareMoodSummary(0.32f, BreakRiskCount: 1, StressedCount: 1, ContentCount: 0)
        };
        Decision decision = Evaluate(briefing);

        decision.Advice.Should().ContainSingle()
            .Which.Priority.Should().Be(AdvicePriority.Critical);
    }

    [Fact]
    public void GrewPastBeds_RequestsOnlyTheBedDeficit()
    {
        Decision decision = Evaluate("grew-past-beds");

        decision.Trace.Should().Be("shelter_floor");
        BuildingRequest request = decision.Flags.Should().ContainSingle().Subject
            .BuildingRequests.Should().ContainSingle().Subject;
        request.CapacityNeed.Should().Be(new CapacityNeed(CapacityMeasure.Beds, 2));
        request.Request.Should().Contain("2 beds");
        decision.Advice.Should().ContainSingle()
            .Which.Body.Should().Contain("2 colonists lack bed capacity");
    }

    [Fact]
    public void UnroofedBedroom_RequestsRoofFramingInsteadOfNewBeds()
    {
        Decision decision = Evaluate("unroofed-bedroom");

        BuildingRequest request = decision.Flags.Should().ContainSingle().Subject
            .BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Roof);
        request.CapacityNeed.Should().Be(new CapacityNeed(CapacityMeasure.Occupants, 3));
        decision.Advice.Should().ContainSingle()
            .Which.Body.Should().Contain("open roof");
    }

    [Fact]
    public void LowJoyWithoutRecreationSource_EmitsRecreationAdviceAndWillieFlag()
    {
        Decision decision = Evaluate("low-joy-no-rec");

        decision.Trace.Should().Be("recreation_gap");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_recreation_gap");
        advice.Priority.Should().Be(AdvicePriority.Medium);
        BuildingRequest request = decision.Flags.Should().ContainSingle().Subject
            .BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Recreation);
        request.TargetDef.Should().Be("HorseshoesPin");
        request.RoomClass.Should().Be(RoomClass.Recreation);
        request.RequestedFrom.Should().Be("Willie");
    }

    [Fact]
    public void LowJoyWithRecreationSource_EmitsAdviceOnly()
    {
        Decision decision = Evaluate("low-joy-has-rec");

        decision.Trace.Should().Be("recreation_gap");
        decision.Advice.Should().ContainSingle()
            .Which.Id.Should().Be("welfare_recreation_gap");
        decision.Flags.Should().BeEmpty();
        decision.Advice[0].Actions.Should().ContainSingle()
            .Which.Kind.Should().Be(AdviceActionKind.Note);
    }

    [Fact]
    public void AteWithoutTable_EmitsComfortBeautyDiningRequest()
    {
        Decision decision = Evaluate("ate-without-table");

        decision.Trace.Should().Be("comfort_beauty");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_comfort_beauty");
        advice.Title.Should().Contain("table");
        BuildingRequest request = decision.Flags.Should().ContainSingle().Subject
            .BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Table);
        request.TargetDef.Should().Be("Table1x2c");
        request.RoomClass.Should().Be(RoomClass.Dining);
    }

    [Fact]
    public void MultiRule_EmitsEveryMatchedRulePrioritySorted()
    {
        Decision decision = Evaluate("multi-rule");

        decision.Trace.Should().Be("rules:shelter_floor+recreation_gap+comfort_beauty");
        decision.Advice.Select(advice => advice.Id)
            .Should().Equal("welfare_shelter_floor", "welfare_recreation_gap", "welfare_comfort_beauty");
        decision.Advice.Select(advice => advice.Priority)
            .Should().Equal(AdvicePriority.High, AdvicePriority.Medium, AdvicePriority.Low);
        decision.Flags.Select(flag => flag.Id)
            .Should().Equal("welfare:shelter_floor", "welfare:recreation_gap", "welfare:comfort_beauty");
        decision.Diagnostics!.EmittedAdvice.Select(row => row.Rule)
            .Should().Equal("shelter_floor", "recreation_gap", "comfort_beauty");
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "recreation_gap" &&
            row.Outcome == "selected");
    }

    private static Decision Evaluate(string fixtureName) =>
        Evaluate(LoadFixture(fixtureName));

    private static Decision Evaluate(WelfareSourceBriefing briefing)
    {
        RulesResult result = new Rules(new FixedTimeProvider(FixedNow)).Evaluate(briefing, ColonyContext.Default);
        return result.Should().BeOfType<Decision>().Subject;
    }

    private static void AssertAllRulesIncludeTableAndStableFallback(Decision decision) =>
        decision.Diagnostics!.AllRules.Select(row => row.Rule)
            .Should().Equal("break_risk", "shelter_floor", "recreation_gap", "comfort_beauty", "needs_stable");

    private static WelfareSourceBriefing LoadFixture(string name)
    {
        string path = FindFixturePath($"{name}.json");
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WelfareSourceBriefing>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Fixture failed to deserialize: {path}");
    }

    private static string FindFixturePath(string fileName)
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Src", "Tests", "Welfare", "Fixtures", fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find Welfare fixture {fileName}.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
