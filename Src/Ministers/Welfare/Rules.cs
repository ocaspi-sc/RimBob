using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Welfare;

public sealed class Rules : IMinisterRules<WelfareSourceBriefing>
{
    private const string MinisterName = "Welfare";
    private const string Domain = "welfare";
    private readonly TimeProvider timeProvider;

    public Rules(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RulesResult Evaluate(WelfareSourceBriefing briefing, ColonyContext context)
    {
        if (briefing.ColonistCount > 0 && briefing.Mood.BreakRiskCount > 0)
            return BreakRiskDecision(briefing);

        if (briefing.DataCoverage.HasRooms &&
            briefing.ColonistCount > 0 &&
            briefing.Rooms.BedroomCount == 0)
        {
            return ShelterFloorDecision(briefing);
        }

        return new Decision(
            [],
            [],
            "needs_stable",
            DiagnosticsFor(briefing, "needs_stable"));
    }

    private Decision BreakRiskDecision(WelfareSourceBriefing briefing)
    {
        AdvicePriority priority = briefing.Mood.BreakRiskCount * 2 >= briefing.ColonistCount
            ? AdvicePriority.Critical
            : AdvicePriority.High;
        WelfarePawnMood? pawn = WorstAtRiskPawn(briefing);
        string pawnName = pawn?.Name ?? "the worst-risk pawn";
        string driver = DominantDriver(pawn);

        return DecisionFor(
            briefing,
            "break_risk",
            WelfareConcern.BreakRisk,
            priority,
            $"{briefing.Mood.BreakRiskCount} colonist{Plural(briefing.Mood.BreakRiskCount)} near mental break",
            $"{pawnName} is the clearest current break-risk example; dominant driver: {driver}.",
            "Immediate mood collapse is the highest-urgency Welfare state. Slice A names the driver but leaves cross-minister routing for the thought taxonomy slice.",
            [new AdviceAction(AdviceActionKind.Note, $"Address {driver} for {pawnName} before a mental break.", Owner: MinisterName)],
            []);
    }

    private Decision ShelterFloorDecision(WelfareSourceBriefing briefing)
    {
        string thoughtEvidence = ShelterThoughtEvidence(briefing);
        string rationale = string.IsNullOrWhiteSpace(thoughtEvidence)
            ? "No bedroom or bed-bearing room is visible in the room briefing."
            : $"No bedroom or bed-bearing room is visible in the room briefing. {thoughtEvidence}";

        BuildingRequest request = new(
            Request: $"basic barracks with {briefing.ColonistCount} beds",
            Reason: "colony has no bedroom/beds; colonists will sleep unsheltered",
            TargetClass: BuildingClass.Bed,
            TargetDef: "Bed",
            RoomClass: RoomClass.Barracks,
            CapacityNeed: new CapacityNeed(CapacityMeasure.Beds, briefing.ColonistCount),
            Priority: AdvicePriority.High,
            RequestedFrom: "Willie");
        DateTimeOffset now = timeProvider.GetUtcNow();
        AgentFlag flag = new(
            Id: "welfare:shelter_floor",
            SourceMinister: MinisterName,
            Severity: FlagSeverity.High,
            Domain: Domain,
            Summary: "Starter barracks needed",
            BuildingRequests: [request],
            Detail: "shelter_floor",
            ExpiresAt: now.AddHours(24));

        return DecisionFor(
            briefing,
            "shelter_floor",
            WelfareConcern.ShelterFloor,
            AdvicePriority.High,
            "Colonists need sleeping shelter",
            $"All {briefing.ColonistCount} colonists have no beds and will sleep unsheltered - mood and rest will suffer.",
            rationale,
            [new AdviceAction(AdviceActionKind.Note, $"Colonists need a roofed barracks with {briefing.ColonistCount} beds; sent to Willie for placement.", Owner: MinisterName)],
            [flag]);
    }

    private Decision DecisionFor(
        WelfareSourceBriefing briefing,
        string trace,
        WelfareConcern concern,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        IReadOnlyList<AgentFlag> flags)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        AdviceItem advice = new(
            Id: $"{Domain}_{trace}",
            Minister: MinisterName,
            Concern: ToSnakeCase(concern.ToString()),
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            Actions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedGameTick: briefing.GameTick,
            ExpiresGameTick: AdviceFreshness.ExpiresGameTick(briefing.GameTick, priority),
            BriefingRef: new BriefingRef(MinisterName, briefing.BriefingVersion, $"welfare:{briefing.BriefingVersion}"));

        RuleTraceDetails diagnostics = DiagnosticsFor(briefing, trace)
            .WithEmissions("rules", trace, [advice], flags);
        return new Decision([advice], flags, trace, diagnostics);
    }

    private static RuleTraceDetails DiagnosticsFor(WelfareSourceBriefing briefing, string selectedRule)
    {
        IReadOnlyList<RuleTraceEntry> matches = RuleMatches(briefing);
        RuleTraceEntry? selected = matches.FirstOrDefault(match =>
            string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<RuleTraceEntry> matchedSignals = selected is null
            ? [new RuleTraceEntry(selectedRule, "selected", "selected by fallthrough")]
            : [selected with { Outcome = "selected" }];
        IReadOnlyList<RuleTraceEntry> suppressed = matches
            .Where(match => !string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase))
            .Select(match => match with { Outcome = "suppressed" })
            .ToList();

        return new RuleTraceDetails(selectedRule, matchedSignals, suppressed)
        {
            AllRules = AllRuleEvaluations(matches, selectedRule)
        };
    }

    private static IReadOnlyList<RuleTraceEntry> RuleMatches(WelfareSourceBriefing briefing)
    {
        List<RuleTraceEntry> matches = [];

        if (briefing.ColonistCount > 0 && briefing.Mood.BreakRiskCount > 0)
            matches.Add(new RuleTraceEntry("break_risk", "matched", $"break_risk_count={briefing.Mood.BreakRiskCount}"));

        if (briefing.DataCoverage.HasRooms &&
            briefing.ColonistCount > 0 &&
            briefing.Rooms.BedroomCount == 0)
        {
            matches.Add(new RuleTraceEntry("shelter_floor", "matched", "bedroom_count=0"));
        }

        return matches;
    }

    private static IReadOnlyList<RuleEvaluationTrace> AllRuleEvaluations(
        IReadOnlyList<RuleTraceEntry> matches,
        string selectedRule)
    {
        Dictionary<string, RuleTraceEntry> outcomes = matches
            .GroupBy(match => match.Rule, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return
        [
            RuleEvaluation("break_risk", outcomes, selectedRule, "colonists > 0 && break_risk_count > 0", "note"),
            RuleEvaluation("shelter_floor", outcomes, selectedRule, "has_rooms && colonists > 0 && bedroom_count == 0", "note + building_request"),
            RuleEvaluation("needs_stable", outcomes, selectedRule, "no deterministic Welfare rule matched", "none")
        ];
    }

    private static RuleEvaluationTrace RuleEvaluation(
        string rule,
        IReadOnlyDictionary<string, RuleTraceEntry> outcomes,
        string selectedRule,
        string conditions,
        string outputAction)
    {
        if (string.Equals(rule, selectedRule, StringComparison.OrdinalIgnoreCase))
            return new RuleEvaluationTrace(rule, "selected", conditions, outputAction, "selected by rule order");

        if (outcomes.TryGetValue(rule, out RuleTraceEntry? trace))
            return new RuleEvaluationTrace(rule, "matched", conditions, outputAction, trace.Reason);

        return new RuleEvaluationTrace(rule, "not_matched", conditions, outputAction, null);
    }

    private static WelfarePawnMood? WorstAtRiskPawn(WelfareSourceBriefing briefing) =>
        briefing.WorstPawns.FirstOrDefault(pawn => pawn.Mood < 0.35f) ??
        briefing.WorstPawns.FirstOrDefault();

    private static string DominantDriver(WelfarePawnMood? pawn)
    {
        if (pawn is null)
            return "mood driver unavailable";

        WelfareMoodThought? thought = pawn.TopNegativeThoughts.FirstOrDefault();
        if (thought is not null)
            return ThoughtLabel(thought);

        return "low mood with no thought detail";
    }

    private static string ShelterThoughtEvidence(WelfareSourceBriefing briefing)
    {
        WelfarePawnMood? pawn = briefing.WorstPawns.FirstOrDefault(pawn =>
            pawn.TopNegativeThoughts.Any(IsShelterSleepThought));
        WelfareMoodThought? thought = pawn?.TopNegativeThoughts.FirstOrDefault(IsShelterSleepThought);
        return pawn is null || thought is null
            ? string.Empty
            : $"{pawn.Name} already reports {ThoughtLabel(thought)}.";
    }

    private static bool IsShelterSleepThought(WelfareMoodThought thought)
    {
        string normalized = $"{thought.DefName} {thought.Label}".ToLowerInvariant()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return normalized.Contains("sleptoutside", StringComparison.Ordinal) ||
               normalized.Contains("sleptonground", StringComparison.Ordinal);
    }

    private static string ThoughtLabel(WelfareMoodThought thought) =>
        string.IsNullOrWhiteSpace(thought.Label)
            ? thought.DefName
            : thought.Label.Trim();

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static string ToSnakeCase(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
