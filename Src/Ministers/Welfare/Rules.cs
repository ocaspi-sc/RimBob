using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Welfare;

public sealed class Rules : IMinisterRules<WelfareSourceBriefing>
{
    private const string MinisterName = "Welfare";
    private const string Domain = "welfare";
    private const float BreakRiskMood = 0.35f;
    private readonly TimeProvider timeProvider;

    public Rules(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RulesResult Evaluate(WelfareSourceBriefing briefing, ColonyContext context)
    {
        IReadOnlyList<ConcernEmission> concerns = DeterministicConcerns(briefing);
        if (concerns.Count > 0)
            return DecisionForConcerns(briefing, concerns);

        return new Decision(
            [],
            [],
            "needs_stable",
            DiagnosticsFor(briefing, "needs_stable"));
    }

    private IReadOnlyList<ConcernEmission> DeterministicConcerns(WelfareSourceBriefing briefing)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ConcernEmission> concerns = [];

        if (ShouldEmitBreakRisk(briefing))
            concerns.Add(BreakRiskConcern(briefing, now));

        if (ShouldEmitShelterFloor(briefing))
            concerns.Add(ShelterFloorConcern(briefing, now));

        if (ShouldEmitRecreationGap(briefing))
            concerns.Add(RecreationGapConcern(briefing, now));

        if (ShouldEmitComfortBeauty(briefing))
            concerns.Add(ComfortBeautyConcern(briefing, now));

        return concerns;
    }

    private static bool ShouldEmitBreakRisk(WelfareSourceBriefing briefing) =>
        briefing.ColonistCount > 0 && briefing.Mood.BreakRiskCount > 0;

    private static bool ShouldEmitShelterFloor(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasRooms &&
        briefing.ColonistCount > 0 &&
        (briefing.Sleep.BedDeficit > 0 || briefing.Sleep.UnroofedBedroomCount > 0);

    private static bool ShouldEmitRecreationGap(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasNeedLevels &&
        briefing.Recreation.JoyLowCount > 0;

    private static bool ShouldEmitComfortBeauty(WelfareSourceBriefing briefing) =>
        ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty) is not null ||
        briefing.NeedLows.Any(need =>
            need.Need.Equals("comfort", StringComparison.OrdinalIgnoreCase) ||
            need.Need.Equals("beauty", StringComparison.OrdinalIgnoreCase));

    private ConcernEmission BreakRiskConcern(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        AdvicePriority priority = briefing.Mood.BreakRiskCount * 2 >= briefing.ColonistCount
            ? AdvicePriority.Critical
            : AdvicePriority.High;
        WelfarePawnMood? pawn = WorstAtRiskPawn(briefing);
        string pawnName = pawn?.Name ?? "the worst-risk pawn";
        DominantDriver driver = DominantDriverFor(pawn);
        AgentFlag? routedFlag = BreakRiskDriverFlag(driver, pawnName, priority, now);

        return ConcernFor(
            briefing,
            now,
            "break_risk",
            WelfareConcern.BreakRisk,
            priority,
            $"{briefing.Mood.BreakRiskCount} colonist{Plural(briefing.Mood.BreakRiskCount)} near mental break",
            $"{pawnName} is the clearest current break-risk example; dominant driver: {driver.Label}.",
            BreakRiskRationale(driver, routedFlag),
            [new AdviceAction(AdviceActionKind.Note, $"Address {driver.Label} for {pawnName} before a mental break.", Owner: MinisterName)],
            routedFlag is null ? [] : [routedFlag]);
    }

    private ConcernEmission ShelterFloorConcern(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        bool needsBeds = briefing.Sleep.BedDeficit > 0;
        int bedNeed = Math.Max(1, briefing.Sleep.BedDeficit);
        AdvicePriority priority = AdvicePriority.High;
        BuildingRequest request = needsBeds
            ? new BuildingRequest(
                Request: $"add {bedNeed} bed{Plural(bedNeed)} in a roofed barracks",
                Reason: $"{bedNeed} colonist{Plural(bedNeed)} lack bed capacity",
                TargetClass: BuildingClass.Bed,
                TargetDef: "Bed",
                RoomClass: RoomClass.Barracks,
                CapacityNeed: new CapacityNeed(CapacityMeasure.Beds, bedNeed),
                Priority: priority,
                RequestedFrom: "Willie")
            : new BuildingRequest(
                Request: "roof or replace unroofed sleeping room",
                Reason: $"{briefing.Sleep.UnroofedBedroomCount} bed-bearing room{Plural(briefing.Sleep.UnroofedBedroomCount)} have open roof tiles",
                TargetClass: BuildingClass.Roof,
                TargetDef: "RoofConstructed",
                RoomClass: RoomClass.Barracks,
                CapacityNeed: new CapacityNeed(CapacityMeasure.Occupants, briefing.ColonistCount),
                Priority: priority,
                RequestedFrom: "Willie");
        AgentFlag flag = BuildFlag(
            "shelter_floor",
            priority,
            needsBeds ? "Sleeping bed capacity needed" : "Sleeping room needs roof",
            now,
            buildingRequests: [request]);

        return ConcernFor(
            briefing,
            now,
            "shelter_floor",
            WelfareConcern.ShelterFloor,
            priority,
            needsBeds ? "Colonists need more sleeping shelter" : "Sleeping room is not fully roofed",
            ShelterBody(briefing, needsBeds, bedNeed),
            ShelterRationale(briefing, needsBeds),
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, ShelterAction(briefing, needsBeds, bedNeed), Owner: "Willie")],
            [flag]);
    }

    private ConcernEmission RecreationGapConcern(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        bool canProveNoSource = briefing.DataCoverage.HasBuildings && !briefing.Recreation.HasRecreationSource;
        AdvicePriority priority = canProveNoSource ? AdvicePriority.Medium : AdvicePriority.Low;
        IReadOnlyList<AgentFlag> flags = canProveNoSource
            ? [BuildFlag(
                "recreation_gap",
                priority,
                "Starter recreation source needed",
                now,
                buildingRequests:
                [
                    new BuildingRequest(
                        Request: $"starter recreation source for {briefing.ColonistCount} colonists",
                        Reason: $"{briefing.Recreation.JoyLowCount} colonist{Plural(briefing.Recreation.JoyLowCount)} have low joy and no recreation source is visible",
                        TargetClass: BuildingClass.Recreation,
                        TargetDef: "HorseshoesPin",
                        RoomClass: RoomClass.Recreation,
                        CapacityNeed: new CapacityNeed(CapacityMeasure.Occupants, Math.Max(1, briefing.ColonistCount)),
                        Priority: priority,
                        RequestedFrom: "Willie")
                ])]
            : [];

        string body = canProveNoSource
            ? $"{briefing.Recreation.JoyLowCount} colonist{Plural(briefing.Recreation.JoyLowCount)} have low joy and no recreation source is visible."
            : $"{briefing.Recreation.JoyLowCount} colonist{Plural(briefing.Recreation.JoyLowCount)} have low joy; recreation time or access needs player review.";
        string rationale = briefing.DataCoverage.HasBuildings
            ? $"Recreation sources visible: {briefing.Recreation.JoySourceBuildingCount} buildings and {briefing.Recreation.RecreationRoomCount} recreation rooms."
            : "Building coverage is missing, so Welfare does not claim the map has no recreation source.";

        return ConcernFor(
            briefing,
            now,
            "recreation_gap",
            WelfareConcern.RecreationGap,
            priority,
            "Recreation need is falling",
            body,
            rationale,
            [new AdviceAction(canProveNoSource ? AdviceActionKind.PlaceBlueprint : AdviceActionKind.Note, RecreationAction(canProveNoSource), Owner: canProveNoSource ? "Willie" : MinisterName)],
            flags);
    }

    private ConcernEmission ComfortBeautyConcern(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        WelfareThoughtGroup? thoughtGroup = ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty);
        bool hasConcreteTableThought = thoughtGroup is not null && IsDiningTablePressure(thoughtGroup.ExampleLabel);
        AdvicePriority priority = AdvicePriority.Low;
        IReadOnlyList<AgentFlag> flags = hasConcreteTableThought
            ? [BuildFlag(
                "comfort_beauty",
                priority,
                "Dining table comfort fix needed",
                now,
                buildingRequests:
                [
                    new BuildingRequest(
                        Request: $"starter dining table for {briefing.ColonistCount} colonists",
                        Reason: $"comfort/beauty thought pressure: {thoughtGroup!.ExampleLabel}",
                        TargetClass: BuildingClass.Table,
                        TargetDef: "Table1x2c",
                        RoomClass: RoomClass.Dining,
                        CapacityNeed: new CapacityNeed(CapacityMeasure.Occupants, Math.Max(1, briefing.ColonistCount)),
                        Priority: priority,
                        RequestedFrom: "Willie")
                ])]
            : [];

        string evidence = thoughtGroup is not null
            ? $"{thoughtGroup.PawnCount} pawn{Plural(thoughtGroup.PawnCount)} report {thoughtGroup.ExampleLabel}."
            : ComfortBeautyLowNeeds(briefing);

        return ConcernFor(
            briefing,
            now,
            "comfort_beauty",
            WelfareConcern.ComfortBeauty,
            priority,
            hasConcreteTableThought ? "Colonists need a table" : "Comfort or beauty need is low",
            hasConcreteTableThought
                ? $"{evidence} A small dining setup removes a cheap recurring mood penalty."
                : $"{evidence} Improve comfort or beauty only where the fix is concrete.",
            "Comfort/beauty pressure is lower urgency than sleep shelter and break risk, but it is a recurring Mood & Needs drag.",
            [new AdviceAction(hasConcreteTableThought ? AdviceActionKind.PlaceBlueprint : AdviceActionKind.Note, ComfortBeautyAction(hasConcreteTableThought), Owner: hasConcreteTableThought ? "Willie" : MinisterName)],
            flags);
    }

    private static Decision DecisionForConcerns(
        WelfareSourceBriefing briefing,
        IReadOnlyList<ConcernEmission> concerns)
    {
        IReadOnlyList<ConcernEmission> orderedConcerns = concerns
            .Select((concern, index) => new { concern, index })
            .OrderByDescending(row => row.concern.Advice.Priority)
            .ThenBy(row => row.index)
            .Select(row => row.concern)
            .ToList();
        IReadOnlyList<AdviceItem> advice = orderedConcerns
            .Select(concern => concern.Advice)
            .ToList();
        IReadOnlyList<AgentFlag> flags = orderedConcerns
            .SelectMany(concern => concern.Flags)
            .ToList();
        string trace = CompositeTrace(orderedConcerns);
        RuleTraceDetails diagnostics = DiagnosticsFor(briefing, trace, orderedConcerns);
        return new Decision(advice, flags, trace, diagnostics);
    }

    private static ConcernEmission ConcernFor(
        WelfareSourceBriefing briefing,
        DateTimeOffset now,
        string trace,
        WelfareConcern concern,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        IReadOnlyList<AgentFlag> flags)
    {
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

        return new ConcernEmission(trace, advice, flags);
    }

    private static AgentFlag BuildFlag(
        string trace,
        AdvicePriority priority,
        string summary,
        DateTimeOffset now,
        IReadOnlyList<BuildingRequest>? buildingRequests = null,
        IReadOnlyList<ItemRequest>? itemRequests = null,
        IReadOnlyList<AttentionRequest>? attention = null) =>
        new(
            Id: $"{Domain}:{trace}",
            SourceMinister: MinisterName,
            Severity: ToFlagSeverity(priority),
            Domain: Domain,
            Summary: summary,
            BuildingRequests: NullIfEmpty(buildingRequests),
            ItemRequests: NullIfEmpty(itemRequests),
            Attention: NullIfEmpty(attention),
            Detail: trace,
            ExpiresAt: now.AddHours(24));

    private static AgentFlag? BreakRiskDriverFlag(
        DominantDriver driver,
        string pawnName,
        AdvicePriority priority,
        DateTimeOffset now)
    {
        string? owner = WelfareThoughtTaxonomy.SuggestedOwner(driver.Category);
        if (owner is null || !IsLiveOwner(owner))
            return null;

        if (owner.Equals("Chef", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFlag(
                "break_risk_chef",
                priority,
                "Break-risk food driver needs Chef",
                now,
                itemRequests:
                [
                    new ItemRequest(
                        Request: $"nutrition-chain fix for {pawnName}",
                        Reason: $"dominant break-risk driver: {driver.Label}",
                        Priority: priority,
                        RequestedFrom: "Chef")
                ]);
        }

        return BuildFlag(
            "break_risk_willie",
            priority,
            "Break-risk room driver needs Willie",
            now,
            attention:
            [
                new AttentionRequest(
                    Request: $"room or building fix for {pawnName}: {driver.Label}",
                    Reason: "dominant break-risk thought maps to a live Willie-owned physical fix",
                    Priority: priority,
                    RequestedFrom: "Willie")
            ]);
    }

    private static RuleTraceDetails DiagnosticsFor(
        WelfareSourceBriefing briefing,
        string selectedRule,
        IReadOnlyList<ConcernEmission>? emissions = null)
    {
        List<RuleTraceEntry> matches = RuleMatches(briefing);
        HashSet<string> emittedRules = emissions is null
            ? [selectedRule]
            : emissions.Select(emission => emission.Rule).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (emissions is null &&
            !matches.Any(match => string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase)))
        {
            matches.Add(new RuleTraceEntry(selectedRule, "selected", "no deterministic Welfare concern matched"));
        }

        List<RuleTraceEntry> annotated = matches
            .Select(match => match with
            {
                Outcome = emittedRules.Contains(match.Rule) ? "selected" : "matched"
            })
            .ToList();

        RuleTraceDetails details = new(selectedRule, annotated, [])
        {
            AllRules = AllRuleEvaluations(annotated)
        };

        return emissions is null ? details : WithConcernEmissions(details, emissions);
    }

    private static RuleTraceDetails WithConcernEmissions(
        RuleTraceDetails details,
        IReadOnlyList<ConcernEmission> emissions)
    {
        List<RuleEmittedAdviceTrace> emittedAdvice = [];
        List<RuleEmittedActionTrace> emittedActions = [];
        List<RuleEmittedFlagTrace> emittedFlags = [];

        foreach (ConcernEmission emission in emissions)
        {
            AdviceItem item = emission.Advice;
            emittedAdvice.Add(new RuleEmittedAdviceTrace(
                Source: "rules",
                Rule: emission.Rule,
                AdviceId: item.Id,
                Concern: item.Concern,
                Priority: item.Priority,
                Title: item.Title,
                ActionCount: item.Actions.Count));

            for (int i = 0; i < item.Actions.Count; i++)
            {
                AdviceAction action = item.Actions[i];
                emittedActions.Add(new RuleEmittedActionTrace(
                    Source: "rules",
                    Rule: emission.Rule,
                    AdviceId: item.Id,
                    ActionIndex: i,
                    Kind: action.Kind,
                    Instruction: action.Instruction,
                    ApplyKind: action.Apply?.Kind,
                    ApplyLabel: action.Apply?.Label,
                    ApplyTargetSummary: action.Apply?.TargetSummary));
            }

            foreach (AgentFlag flag in emission.Flags)
            {
                emittedFlags.Add(new RuleEmittedFlagTrace(
                    Source: "rules",
                    Rule: emission.Rule,
                    FlagId: flag.Id,
                    Severity: flag.Severity,
                    Summary: flag.Summary,
                    RequestCount:
                        (flag.BuildingRequests?.Count ?? 0) +
                        (flag.LaborRequests?.Count ?? 0) +
                        (flag.ItemRequests?.Count ?? 0) +
                        (flag.Attention?.Count ?? 0)));
            }
        }

        return details with
        {
            EmittedAdvice = emittedAdvice,
            EmittedActions = emittedActions,
            EmittedFlags = emittedFlags
        };
    }

    private static List<RuleTraceEntry> RuleMatches(WelfareSourceBriefing briefing)
    {
        List<RuleTraceEntry> matches = [];

        if (ShouldEmitBreakRisk(briefing))
            matches.Add(Match("break_risk", $"break_risk_count={briefing.Mood.BreakRiskCount}"));

        if (ShouldEmitShelterFloor(briefing))
        {
            matches.Add(Match(
                "shelter_floor",
                $"bed_deficit={briefing.Sleep.BedDeficit}; unroofed_bedrooms={briefing.Sleep.UnroofedBedroomCount}"));
        }

        if (ShouldEmitRecreationGap(briefing))
            matches.Add(Match("recreation_gap", $"joy_low_count={briefing.Recreation.JoyLowCount}"));

        if (ShouldEmitComfortBeauty(briefing))
        {
            WelfareThoughtGroup? group = ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty);
            matches.Add(Match(
                "comfort_beauty",
                group is null ? "comfort_or_beauty_need_low=true" : $"thought={group.ExampleLabel}; pawns={group.PawnCount}"));
        }

        return matches;
    }

    private static IReadOnlyList<RuleEvaluationTrace> AllRuleEvaluations(IReadOnlyList<RuleTraceEntry> annotated)
    {
        Dictionary<string, RuleTraceEntry> outcomes = annotated.ToDictionary(
            entry => entry.Rule,
            StringComparer.OrdinalIgnoreCase);

        return
        [
            RuleEvaluation("break_risk", outcomes,
                "colonists > 0 && break_risk_count > 0",
                "BreakRisk advice; optional live-owner routing flag"),
            RuleEvaluation("shelter_floor", outcomes,
                "has_rooms && colonists > 0 && (bed_deficit > 0 || unroofed_bedroom_count > 0)",
                "ShelterFloor advice; Willie building_request"),
            RuleEvaluation("recreation_gap", outcomes,
                "has_need_levels && joy_low_count > 0",
                "RecreationGap advice; Willie building_request only when building coverage proves no recreation source"),
            RuleEvaluation("comfort_beauty", outcomes,
                "comfort/beauty thought category exists || comfort/beauty need low",
                "ComfortBeauty advice; Willie dining/table building_request when the thought is concrete"),
            RuleEvaluation("needs_stable", outcomes,
                "no deterministic Welfare rule matched",
                "No advice")
        ];
    }

    private static RuleEvaluationTrace RuleEvaluation(
        string rule,
        IReadOnlyDictionary<string, RuleTraceEntry> outcomes,
        string conditions,
        string outputAction)
    {
        if (outcomes.TryGetValue(rule, out RuleTraceEntry? trace))
            return new RuleEvaluationTrace(rule, trace.Outcome, conditions, outputAction, trace.Reason);

        return new RuleEvaluationTrace(rule, "not_matched", conditions, outputAction, null);
    }

    private static RuleTraceEntry Match(string rule, string reason) =>
        new(rule, "matched", reason);

    private static string CompositeTrace(IReadOnlyList<ConcernEmission> concerns) =>
        concerns.Count == 1
            ? concerns[0].Rule
            : $"concerns:{string.Join("+", concerns.Select(concern => concern.Rule))}";

    private static string ShelterBody(WelfareSourceBriefing briefing, bool needsBeds, int bedNeed)
    {
        if (needsBeds && briefing.Sleep.BedCount == 0)
            return $"All {briefing.ColonistCount} colonists have no beds and will sleep unsheltered - mood and rest will suffer.";

        if (needsBeds)
            return $"{bedNeed} colonist{Plural(bedNeed)} lack bed capacity; {briefing.ColonistCount} colonists share {briefing.Sleep.BedCount} bed{Plural(briefing.Sleep.BedCount)}.";

        return $"{briefing.Sleep.UnroofedBedroomCount} sleeping room{Plural(briefing.Sleep.UnroofedBedroomCount)} have open roof tiles, so sleep quality is still exposed.";
    }

    private static string ShelterRationale(WelfareSourceBriefing briefing, bool needsBeds)
    {
        string thoughtEvidence = ShelterThoughtEvidence(briefing);
        string fieldEvidence = needsBeds
            ? $"Bed count {briefing.Sleep.BedCount} is below colonist count {briefing.ColonistCount}."
            : $"{briefing.Sleep.UnroofedBedroomCount} bed-bearing room{Plural(briefing.Sleep.UnroofedBedroomCount)} report open roof tiles.";
        return string.IsNullOrWhiteSpace(thoughtEvidence)
            ? fieldEvidence
            : $"{fieldEvidence} {thoughtEvidence}";
    }

    private static string ShelterAction(WelfareSourceBriefing briefing, bool needsBeds, int bedNeed) =>
        needsBeds
            ? $"Ask Willie for {bedNeed} more bed{Plural(bedNeed)} in a roofed barracks."
            : $"Ask Willie to roof or replace {briefing.Sleep.UnroofedBedroomCount} exposed sleeping room{Plural(briefing.Sleep.UnroofedBedroomCount)}.";

    private static string RecreationAction(bool canProveNoSource) =>
        canProveNoSource
            ? "Ask Willie for a cheap starter recreation source."
            : "Check joy time and access before building more recreation.";

    private static string ComfortBeautyAction(bool hasConcreteTableThought) =>
        hasConcreteTableThought
            ? "Ask Willie for a small dining table and seats."
            : "Address the worst concrete comfort or beauty source before spending broadly.";

    private static string ComfortBeautyLowNeeds(WelfareSourceBriefing briefing)
    {
        IReadOnlyList<WelfareNeedLow> lows = briefing.NeedLows
            .Where(need =>
                need.Need.Equals("comfort", StringComparison.OrdinalIgnoreCase) ||
                need.Need.Equals("beauty", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        return lows.Count == 0
            ? "Comfort/beauty pressure is present but the exact pawn need is not exposed."
            : string.Join("; ", lows.Select(need => $"{need.PawnName} {need.Need} {need.Value:P0}"));
    }

    private static string BreakRiskRationale(DominantDriver driver, AgentFlag? routedFlag)
    {
        string category = ToSnakeCase(driver.Category.ToString());
        if (routedFlag is null)
            return $"The dominant thought maps to {category}; no live owner can take a concrete routed request yet.";

        return $"The dominant thought maps to {category}; Welfare routed a compact request to the live owning minister.";
    }

    private static string ShelterThoughtEvidence(WelfareSourceBriefing briefing)
    {
        WelfarePawnMood? pawn = briefing.WorstPawns.FirstOrDefault(pawn =>
            pawn.TopNegativeThoughts.Any(thought => WelfareThoughtTaxonomy.Classify(thought.DefName, thought.Label) == ThoughtCategory.ShelterSleep));
        WelfareMoodThought? thought = pawn?.TopNegativeThoughts.FirstOrDefault(thought =>
            WelfareThoughtTaxonomy.Classify(thought.DefName, thought.Label) == ThoughtCategory.ShelterSleep);
        return pawn is null || thought is null
            ? string.Empty
            : $"{pawn.Name} already reports {ThoughtLabel(thought)}.";
    }

    private static WelfarePawnMood? WorstAtRiskPawn(WelfareSourceBriefing briefing) =>
        briefing.WorstPawns.FirstOrDefault(pawn => pawn.Mood < BreakRiskMood) ??
        briefing.WorstPawns.FirstOrDefault();

    private static DominantDriver DominantDriverFor(WelfarePawnMood? pawn)
    {
        if (pawn is null)
            return new DominantDriver("mood driver unavailable", ThoughtCategory.Other);

        WelfareMoodThought? thought = pawn.TopNegativeThoughts.FirstOrDefault();
        if (thought is null)
            return new DominantDriver("low mood with no thought detail", ThoughtCategory.Other);

        return new DominantDriver(
            ThoughtLabel(thought),
            WelfareThoughtTaxonomy.Classify(thought.DefName, thought.Label));
    }

    private static WelfareThoughtGroup? ThoughtGroup(WelfareSourceBriefing briefing, ThoughtCategory category) =>
        briefing.ThoughtDigest.ByCategory.FirstOrDefault(group => group.Category == category);

    private static bool IsDiningTablePressure(string label)
    {
        string normalized = label.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        return normalized.Contains("withouttable", StringComparison.Ordinal) ||
               normalized.Contains("onfloor", StringComparison.Ordinal) ||
               normalized.Contains("table", StringComparison.Ordinal);
    }

    private static bool IsLiveOwner(string owner) =>
        owner.Equals("Chef", StringComparison.OrdinalIgnoreCase) ||
        owner.Equals("Willie", StringComparison.OrdinalIgnoreCase);

    private static FlagSeverity ToFlagSeverity(AdvicePriority priority) => priority switch
    {
        AdvicePriority.Critical => FlagSeverity.Critical,
        AdvicePriority.High => FlagSeverity.High,
        AdvicePriority.Medium => FlagSeverity.Medium,
        _ => FlagSeverity.Low
    };

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? values) =>
        values is null || values.Count == 0 ? null : values;

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

    private sealed record ConcernEmission(
        string Rule,
        AdviceItem Advice,
        IReadOnlyList<AgentFlag> Flags);

    private sealed record DominantDriver(
        string Label,
        ThoughtCategory Category);
}
