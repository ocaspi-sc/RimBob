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
        ProjectedRuleRun decision = Evaluate("new-colony");

        decision.Trace.Should().Be("shelter_floor");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_shelter_floor");
        advice.Minister.Should().Be("Welfare");
        advice.Priority.Should().Be(Priority.High);
        advice.Body.Should().Contain("All 3 colonists");
        advice.Rationale.Should().Contain("slept on ground");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:shelter_floor");
        flag.SourceMinister.Should().Be("Welfare");
        flag.Priority.Should().Be(Priority.High);
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
            row.Outcome == RuleOutcome.Selected);
        RuleEvaluationTrace shelterTrace = decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "shelter_floor" &&
            row.Outcome == RuleOutcome.Selected).Subject;
        shelterTrace.Emissions.Should().Contain(emission =>
            emission.Kind == "advise" &&
            emission.Label == advice.Title &&
            emission.Priority == Priority.High);
        shelterTrace.Emissions.Should().Contain(emission =>
            emission.Kind == "request_build" &&
            emission.Label == request.Request &&
            emission.Priority == Priority.High &&
            emission.To == "Willie" &&
            emission.TargetDef == "Bed" &&
            emission.WorkType == null);
        decision.Effects.OfType<RequestBuild>().Should().ContainSingle(effect =>
            effect.Rule == "shelter_floor" &&
            effect.Request.TargetClass == BuildingClass.Bed);
    }

    [Fact]
    public void HasBedsAndNoBreakRisk_ReturnsEmptyStableDecision()
    {
        ProjectedRuleRun decision = Evaluate("has-beds");

        decision.Trace.Should().Be("needs_stable");
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Diagnostics.Should().NotBeNull();
        AssertAllRulesIncludeTableAndStableFallback(decision);
        decision.Diagnostics!.AllRules.Should().Contain(row =>
            row.Rule == "needs_stable" &&
            row.Outcome == RuleOutcome.Selected);
    }

    [Fact]
    public void SocialPressureWithoutWiredRule_Escalates()
    {
        WelfareSourceBriefing briefing = SocialPressureBriefing(LoadFixture("has-beds"));
        RuleRun result = RawEvaluate(briefing);

        Escalate escalation = result.Decisions.Should().ContainSingle()
            .Which.Should().BeOfType<Escalate>().Subject;
        escalation.Rule.Value.Should().Be("unexplained_mood_pressure");
        escalation.Reason.Should().Contain("dominant_unwired_thought=social");
        result.Diagnostics.SelectedRule!.Value.Value.Should().Be("unexplained_mood_pressure");
        result.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "unexplained_mood_pressure" &&
            row.Outcome == RuleOutcome.Escalated);
    }

    [Fact]
    public void WiredRulePresent_DoesNotEscalateForStraySocialThought()
    {
        WelfareSourceBriefing briefing = SocialPressureBriefing(LoadFixture("new-colony"));
        RuleRun result = RawEvaluate(briefing);

        result.Decisions.OfType<Escalate>().Should().BeEmpty();
        result.Decisions.OfType<Advise>().Should().Contain(advice => advice.Rule == "shelter_floor");
        result.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "shelter_floor" &&
            row.Outcome == RuleOutcome.Selected);
        result.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "unexplained_mood_pressure" &&
            row.Outcome == RuleOutcome.NotMatched);
    }

    [Fact]
    public void BreakRisk_EmitsMoodAdviceWithoutBuildFlag()
    {
        ProjectedRuleRun decision = Evaluate("break-risk");

        decision.Trace.Should().Be("rules:break_risk+break_risk_willie");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_break_risk");
        advice.Priority.Should().Be(Priority.High);
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
        ProjectedRuleRun decision = Evaluate(briefing);

        decision.Advice.Should().ContainSingle()
            .Which.Priority.Should().Be(Priority.Critical);
    }

    [Fact]
    public void GrewPastBeds_RequestsOnlyTheBedDeficit()
    {
        ProjectedRuleRun decision = Evaluate("grew-past-beds");

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
        ProjectedRuleRun decision = Evaluate("unroofed-bedroom");

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
        ProjectedRuleRun decision = Evaluate("low-joy-no-rec");

        decision.Trace.Should().Be("recreation_gap");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_recreation_gap");
        advice.Priority.Should().Be(Priority.Medium);
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
        ProjectedRuleRun decision = Evaluate("low-joy-has-rec");

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
        ProjectedRuleRun decision = Evaluate("ate-without-table");

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
        ProjectedRuleRun decision = Evaluate("multi-rule");

        decision.Trace.Should().Be("rules:shelter_floor+recreation_gap+comfort_beauty");
        decision.Advice.Select(advice => advice.Id)
            .Should().Equal("welfare_shelter_floor", "welfare_recreation_gap", "welfare_comfort_beauty");
        decision.Advice.Select(advice => advice.Priority)
            .Should().Equal(Priority.High, Priority.Medium, Priority.Low);
        decision.Flags.Select(flag => flag.Id)
            .Should().Equal("welfare:shelter_floor", "welfare:recreation_gap", "welfare:comfort_beauty");
        decision.Effects.OfType<Advise>().Select(effect => effect.Rule.Value)
            .Should().Equal("shelter_floor", "recreation_gap", "comfort_beauty");
        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "recreation_gap" &&
            row.Outcome == RuleOutcome.Selected);
    }

    [Fact]
    public void TooCold_EmitsTemperatureComfortAdviceAndHeaterRequest()
    {
        ProjectedRuleRun decision = Evaluate("too-cold");

        decision.Trace.Should().Be("temperature_comfort");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_temperature_comfort");
        advice.Priority.Should().Be(Priority.High); // 2 pawns > 1 → High
        advice.Title.Should().Contain("cold");
        advice.Body.Should().Contain("slept in cold");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:temperature_comfort");
        BuildingRequest request = flag.BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Heater);
        request.TargetDef.Should().Be("Heater");
        request.RoomClass.Should().Be(RoomClass.Barracks);
        request.RequestedFrom.Should().Be("Willie");

        decision.Effects.OfType<Escalate>().Should().BeEmpty();
        AssertAllRulesIncludeTableAndStableFallback(decision);
    }

    [Fact]
    public void TooHot_EmitsTemperatureComfortAdviceAndCoolerRequest()
    {
        ProjectedRuleRun decision = Evaluate("too-hot");

        decision.Trace.Should().Be("temperature_comfort");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_temperature_comfort");
        advice.Priority.Should().Be(Priority.High); // 2 pawns > 1 → High
        advice.Title.Should().Contain("heat");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.PlaceBlueprint);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:temperature_comfort");
        BuildingRequest request = flag.BuildingRequests.Should().ContainSingle().Subject;
        request.TargetClass.Should().Be(BuildingClass.Cooler);
        request.TargetDef.Should().Be("Cooler");
        request.RequestedFrom.Should().Be("Willie");

        decision.Effects.OfType<Escalate>().Should().BeEmpty();
    }

    [Fact]
    public void AmbiguousTemperature_EmitsRequestAttentionNoRequestBuild()
    {
        ProjectedRuleRun decision = Evaluate("temperature-ambiguous");

        decision.Trace.Should().Be("temperature_comfort");
        AdviceItem advice = decision.Advice.Should().ContainSingle().Subject;
        advice.Id.Should().Be("welfare_temperature_comfort");
        advice.Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.Note);

        AgentFlag flag = decision.Flags.Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:temperature_comfort");
        flag.BuildingRequests.Should().BeNullOrEmpty();
        flag.Attention.Should().ContainSingle()
            .Which.RequestedFrom.Should().Be("Willie");

        decision.Effects.OfType<Escalate>().Should().BeEmpty();
    }

    [Fact]
    public void MildTemperature_ProducesNoRuleNoEscalate()
    {
        // Offset -1 is above match threshold (-3) so temperature_comfort doesn't fire.
        // After WD2 Temperature is excluded from escalation, so it also doesn't escalate.
        ProjectedRuleRun decision = Evaluate("mild-temperature");

        decision.Trace.Should().Be("needs_stable");
        decision.Advice.Should().BeEmpty();
        decision.Flags.Should().BeEmpty();
        decision.Effects.OfType<Escalate>().Should().BeEmpty();
    }

    [Fact]
    public void SocialOnlyWithoutTemperature_StillEscalates()
    {
        // Social stays LLM-served; confirms temperature exclusion doesn't bleed into social.
        RuleRun result = RawEvaluate(LoadFixture("social-only"));

        Escalate escalation = result.Decisions.Should().ContainSingle()
            .Which.Should().BeOfType<Escalate>().Subject;
        escalation.Rule.Value.Should().Be("unexplained_mood_pressure");
        escalation.Reason.Should().Contain("dominant_unwired_thought=social");
    }

    [Fact]
    public void ShelterGapAndCold_EmitsBothRules()
    {
        ProjectedRuleRun decision = Evaluate("cold-with-shelter-gap");

        decision.Trace.Should().Be("rules:shelter_floor+temperature_comfort");
        decision.Advice.Select(advice => advice.Id)
            .Should().Equal("welfare_shelter_floor", "welfare_temperature_comfort");
        decision.Flags.Select(flag => flag.Id)
            .Should().Equal("welfare:shelter_floor", "welfare:temperature_comfort");
        decision.Flags[0].BuildingRequests.Should().ContainSingle(r => r.TargetClass == BuildingClass.Bed);
        decision.Flags[1].BuildingRequests.Should().ContainSingle(r => r.TargetClass == BuildingClass.Heater);
        decision.Effects.OfType<Escalate>().Should().BeEmpty();
    }

    [Fact]
    public void TemperatureComfort_SeverityDeterminesPriority()
    {
        WelfareThoughtDigest coldDigest(int pawns, float offset) =>
            new([new WelfareThoughtGroup(ThoughtCategory.Temperature, pawns, offset, "slept in cold")]);
        WelfareSourceBriefing base_ = LoadFixture("too-cold");

        // single pawn, non-severe offset → Medium
        ProjectedRuleRun medium = Evaluate(base_ with { ThoughtDigest = coldDigest(1, -5f) });
        medium.Advice.Should().ContainSingle().Which.Priority.Should().Be(Priority.Medium);

        // single pawn, severe offset (≤ -8) → High
        ProjectedRuleRun severe = Evaluate(base_ with { ThoughtDigest = coldDigest(1, -9f) });
        severe.Advice.Should().ContainSingle().Which.Priority.Should().Be(Priority.High);
    }

    private static ProjectedRuleRun Evaluate(string fixtureName) =>
        Evaluate(LoadFixture(fixtureName));

    private static ProjectedRuleRun Evaluate(WelfareSourceBriefing briefing)
    {
        RuleRun result = RawEvaluate(briefing);
        return result.ProjectFor("Welfare", "welfare", briefing.BriefingVersion, null, briefing.GameTick, FixedNow);
    }

    private static RuleRun RawEvaluate(WelfareSourceBriefing briefing) =>
        new Rules(new FixedTimeProvider(FixedNow)).Evaluate(briefing, ColonyContext.Default);

    private static void AssertAllRulesIncludeTableAndStableFallback(ProjectedRuleRun decision) =>
        decision.Diagnostics!.AllRules.Select(row => row.Rule)
            .Should().Equal("break_risk", "shelter_floor", "recreation_gap", "comfort_beauty", "temperature_comfort", "unexplained_mood_pressure", "needs_stable");

    private static WelfareSourceBriefing SocialPressureBriefing(WelfareSourceBriefing briefing) =>
        briefing with
        {
            Mood = new WelfareMoodSummary(
                AverageMood: Math.Min(briefing.Mood.AverageMood, 0.58f),
                BreakRiskCount: 0,
                StressedCount: 1,
                ContentCount: briefing.Mood.ContentCount),
            WorstPawns =
            [
                new WelfarePawnMood(
                    Id: "p1",
                    Name: "Alice",
                    Mood: 0.46f,
                    Sleep: 0.7f,
                    Comfort: 0.7f,
                    Beauty: 0.7f,
                    Joy: 0.7f,
                    FreshAir: 0.7f,
                    DrugsDesire: 0f,
                    TopNegativeThoughts:
                    [
                        new WelfareMoodThought("SocialFight", "social fight", -5f, 0)
                    ])
            ],
            ThoughtDigest = new WelfareThoughtDigest(
            [
                new WelfareThoughtGroup(ThoughtCategory.Social, 1, -5f, "social fight")
            ]),
            DataCoverage = briefing.DataCoverage with { HasMoodThoughts = true }
        };

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
