using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using System.Globalization;

namespace RimBob.Ministers.Welfare;

public sealed class Rules : IMinisterRules<WelfareSourceBriefing>
{
    private const string MinisterName = "Welfare";
    private const string Domain = "welfare";
    private const float BreakRiskMood = 0.35f;
    private const float EscalationMoodFloor = 0.45f;
    private const float MaterialThoughtOffset = -3f;
    private readonly TimeProvider timeProvider;

    public Rules(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RuleRun Evaluate(WelfareSourceBriefing briefing, ColonyContext context)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<MinisterRule<WelfareSourceBriefing>> rules = RuleTable(now);
        IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> fallbackRuleDescriptors = FallbackRuleDescriptors();
        RuleRun ruleRun = MinisterRuleTableEvaluator.EvaluateAllHits(
            rules,
            briefing,
            additionalRuleDescriptors: fallbackRuleDescriptors);
        if (ruleRun.Decisions.Count > 0)
            return ruleRun;

        if (HasUnexplainedMoodPressure(briefing))
        {
            string reason = UnexplainedMoodPressureReason(briefing);
            return new RuleRun(
                [new Escalate("unexplained_mood_pressure", reason, BuildEscalationContext(briefing))],
                DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, "unexplained_mood_pressure"));
        }

        return new RuleRun([], DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, "needs_stable"));
    }

    private IReadOnlyList<MinisterRule<WelfareSourceBriefing>> RuleTable(DateTimeOffset now) =>
    [
        new("break_risk", MatchesBreakRisk, BreakRiskReason, briefing => BreakRiskEmission(briefing, now)),
        new("shelter_floor", MatchesShelterFloor, ShelterFloorReason, briefing => ShelterFloorEmission(briefing, now)),
        new("recreation_gap", MatchesRecreationGap, RecreationGapReason, briefing => RecreationGapEmission(briefing, now)),
        new("comfort_beauty", MatchesComfortBeauty, ComfortBeautyReason, briefing => ComfortBeautyEmission(briefing, now))
    ];

    private static bool MatchesBreakRisk(WelfareSourceBriefing briefing) =>
        briefing.Mood.BreakRiskCount > 0;

    private static string BreakRiskReason(WelfareSourceBriefing briefing) =>
        $"break_risk_count={briefing.Mood.BreakRiskCount}";

    private static bool MatchesShelterFloor(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasRooms &&
        briefing.ColonistCount > 0 &&
        (briefing.Sleep.BedDeficit > 0 || briefing.Sleep.UnroofedBedroomCount > 0);

    private static string ShelterFloorReason(WelfareSourceBriefing briefing)
    {
        if (!briefing.DataCoverage.HasRooms)
            return "room coverage unavailable for shelter floor check";

        if (briefing.ColonistCount <= 0)
            return "no colonists need sleeping shelter";

        return $"bed_deficit={briefing.Sleep.BedDeficit}; unroofed_bedrooms={briefing.Sleep.UnroofedBedroomCount}";
    }

    private static bool MatchesRecreationGap(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasNeedLevels &&
        briefing.Recreation.JoyLowCount > 0;

    private static string RecreationGapReason(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasNeedLevels
            ? $"joy_low_count={briefing.Recreation.JoyLowCount}"
            : "need-level coverage unavailable for recreation check";

    private static bool MatchesComfortBeauty(WelfareSourceBriefing briefing) =>
        ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty) is not null ||
        briefing.NeedLows.Any(need =>
            need.Need.Equals("comfort", StringComparison.OrdinalIgnoreCase) ||
            need.Need.Equals("beauty", StringComparison.OrdinalIgnoreCase));

    private static string ComfortBeautyReason(WelfareSourceBriefing briefing)
    {
        WelfareThoughtGroup? group = ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty);
        if (group is not null)
            return $"thought={group.ExampleLabel}; pawns={group.PawnCount}";

        bool hasComfortOrBeautyNeedLow = briefing.NeedLows.Any(need =>
            need.Need.Equals("comfort", StringComparison.OrdinalIgnoreCase) ||
            need.Need.Equals("beauty", StringComparison.OrdinalIgnoreCase));
        return hasComfortOrBeautyNeedLow
            ? "comfort_or_beauty_need_low=true"
            : "comfort_or_beauty_need_low=false";
    }

    private IReadOnlyList<Decision> BreakRiskEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        Priority priority = briefing.Mood.BreakRiskCount * 2 >= briefing.ColonistCount
            ? Priority.Critical
            : Priority.High;
        WelfarePawnMood? pawn = WorstAtRiskPawn(briefing);
        string pawnName = pawn?.Name ?? "the worst-risk pawn";
        DominantDriver driver = DominantDriverFor(pawn);
        IReadOnlyList<Decision> routedRequests = BreakRiskDriverRequests(driver, pawnName, priority);

        return EmitAdvice(
            briefing,
            now,
            "break_risk",
            priority,
            $"{briefing.Mood.BreakRiskCount} colonist{Plural(briefing.Mood.BreakRiskCount)} near mental break",
            $"{pawnName} is the clearest current break-risk example; dominant driver: {driver.Label}.",
            BreakRiskRationale(driver, routedRequests.Count > 0),
            [new AdviceAction(AdviceActionKind.Note, $"Address {driver.Label} for {pawnName} before a mental break.", Owner: MinisterName)],
            routedRequests);
    }

    private IReadOnlyList<Decision> ShelterFloorEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        bool needsBeds = briefing.Sleep.BedDeficit > 0;
        int bedNeed = Math.Max(1, briefing.Sleep.BedDeficit);
        Priority priority = Priority.High;
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
        IReadOnlyList<Decision> requests = BuildRequestDecisions(
            "shelter_floor",
            priority,
            buildingRequests: [request]);

        return EmitAdvice(
            briefing,
            now,
            "shelter_floor",
            priority,
            needsBeds ? "Colonists need more sleeping shelter" : "Sleeping room is not fully roofed",
            ShelterBody(briefing, needsBeds, bedNeed),
            ShelterRationale(briefing, needsBeds),
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, ShelterAction(briefing, needsBeds, bedNeed), Owner: "Willie")],
            requests);
    }

    private IReadOnlyList<Decision> RecreationGapEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        bool canProveNoSource = briefing.DataCoverage.HasBuildings && !briefing.Recreation.HasRecreationSource;
        Priority priority = canProveNoSource ? Priority.Medium : Priority.Low;
        IReadOnlyList<Decision> requests = canProveNoSource
            ? BuildRequestDecisions(
                "recreation_gap",
                priority,
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
                ])
            : [];

        string body = canProveNoSource
            ? $"{briefing.Recreation.JoyLowCount} colonist{Plural(briefing.Recreation.JoyLowCount)} have low joy and no recreation source is visible."
            : $"{briefing.Recreation.JoyLowCount} colonist{Plural(briefing.Recreation.JoyLowCount)} have low joy; recreation time or access needs player review.";
        string rationale = briefing.DataCoverage.HasBuildings
            ? $"Recreation sources visible: {briefing.Recreation.JoySourceBuildingCount} buildings and {briefing.Recreation.RecreationRoomCount} recreation rooms."
            : "Building coverage is missing, so Welfare does not claim the map has no recreation source.";

        return EmitAdvice(
            briefing,
            now,
            "recreation_gap",
            priority,
            "Recreation need is falling",
            body,
            rationale,
            [new AdviceAction(canProveNoSource ? AdviceActionKind.PlaceBlueprint : AdviceActionKind.Note, RecreationAction(canProveNoSource), Owner: canProveNoSource ? "Willie" : MinisterName)],
            requests);
    }

    private IReadOnlyList<Decision> ComfortBeautyEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        WelfareThoughtGroup? thoughtGroup = ThoughtGroup(briefing, ThoughtCategory.ComfortBeauty);
        bool hasConcreteTableThought = thoughtGroup is not null && IsDiningTablePressure(thoughtGroup.ExampleLabel);
        Priority priority = Priority.Low;
        IReadOnlyList<Decision> requests = hasConcreteTableThought
            ? BuildRequestDecisions(
                "comfort_beauty",
                priority,
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
                ])
            : [];

        string evidence = thoughtGroup is not null
            ? $"{thoughtGroup.PawnCount} pawn{Plural(thoughtGroup.PawnCount)} report {thoughtGroup.ExampleLabel}."
            : ComfortBeautyLowNeeds(briefing);

        return EmitAdvice(
            briefing,
            now,
            "comfort_beauty",
            priority,
            hasConcreteTableThought ? "Colonists need a table" : "Comfort or beauty need is low",
            hasConcreteTableThought
                ? $"{evidence} A small dining setup removes a cheap recurring mood penalty."
                : $"{evidence} Improve comfort or beauty only where the fix is concrete.",
            "Comfort/beauty pressure is lower urgency than sleep shelter and break risk, but it is a recurring Mood & Needs drag.",
            [new AdviceAction(hasConcreteTableThought ? AdviceActionKind.PlaceBlueprint : AdviceActionKind.Note, ComfortBeautyAction(hasConcreteTableThought), Owner: hasConcreteTableThought ? "Willie" : MinisterName)],
            requests);
    }

    private static IReadOnlyList<Decision> EmitAdvice(
        WelfareSourceBriefing briefing,
        DateTimeOffset now,
        string trace,
        Priority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        IReadOnlyList<Decision> requestDecisions)
    {
        return
        [
            new Advise(
                trace,
                priority,
                title,
                body,
                rationale,
                actions),
            ..requestDecisions
        ];
    }

    private static IReadOnlyList<Decision> BuildRequestDecisions(
        string trace,
        Priority priority,
        IReadOnlyList<BuildingRequest>? buildingRequests = null,
        IReadOnlyList<ItemRequest>? itemRequests = null,
        IReadOnlyList<AttentionRequest>? attention = null)
    {
        List<Decision> decisions = [];
        decisions.AddRange((buildingRequests ?? []).Select(request => new RequestBuild(
            trace,
            request,
            request.RequestedFrom ?? "Willie",
            request.Priority ?? priority)));
        decisions.AddRange((itemRequests ?? []).Select(request => new RequestItem(
            trace,
            request,
            request.RequestedFrom ?? "Chief of Staff",
            request.Priority ?? priority)));
        decisions.AddRange((attention ?? []).Select(request => new RequestAttention(
            trace,
            request,
            request.RequestedFrom ?? "Chief of Staff",
            request.Priority ?? priority)));
        return decisions;
    }

    private static IReadOnlyList<Decision> BreakRiskDriverRequests(
        DominantDriver driver,
        string pawnName,
        Priority priority)
    {
        string? owner = WelfareThoughtTaxonomy.SuggestedOwner(driver.Category);
        if (owner is null || !IsLiveOwner(owner))
            return [];

        if (owner.Equals("Chef", StringComparison.OrdinalIgnoreCase))
        {
            return BuildRequestDecisions(
                "break_risk_chef",
                priority,
                itemRequests:
                [
                    new ItemRequest(
                        Request: $"nutrition-chain fix for {pawnName}",
                        Reason: $"dominant break-risk driver: {driver.Label}",
                        Priority: priority,
                        RequestedFrom: "Chef")
                ]);
        }

        return BuildRequestDecisions(
            "break_risk_willie",
            priority,
            attention:
            [
                new AttentionRequest(
                    Request: $"room or building fix for {pawnName}: {driver.Label}",
                    Reason: "dominant break-risk thought maps to a live Willie-owned physical fix",
                    Priority: priority,
                    RequestedFrom: "Willie")
            ]);
    }

    private static RuleTraceDetails DiagnosticsForFallback(
        IReadOnlyList<MinisterRule<WelfareSourceBriefing>> rules,
        IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> fallbackRuleDescriptors,
        WelfareSourceBriefing briefing,
        string selectedRule) =>
        MinisterRuleTableEvaluator.BuildTrace(
            rules,
            briefing,
            decisions: [],
            matchedRules: new HashSet<RuleId> { selectedRule },
            additionalRuleDescriptors: fallbackRuleDescriptors,
            selectedRuleOverride: selectedRule);

    private static IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> FallbackRuleDescriptors() =>
    [
        new("unexplained_mood_pressure", UnexplainedMoodPressureReason, (_, selectedRule) => EscalatedWhenSelected("unexplained_mood_pressure", selectedRule)),
        new("needs_stable", NeedsStableReason, (_, selectedRule) => SelectedWhenSelected("needs_stable", selectedRule))
    ];

    private static string NeedsStableReason(WelfareSourceBriefing briefing) =>
        "no deterministic Welfare rule matched";

    private static RuleOutcome EscalatedWhenSelected(RuleId rule, RuleId? selectedRule) =>
        selectedRule == rule ? RuleOutcome.Escalated : RuleOutcome.NotMatched;

    private static RuleOutcome SelectedWhenSelected(RuleId rule, RuleId? selectedRule) =>
        selectedRule == rule ? RuleOutcome.Selected : RuleOutcome.NotMatched;

    private static bool HasUnexplainedMoodPressure(WelfareSourceBriefing briefing)
    {
        if (briefing.ColonistCount <= 0)
            return false;

        return DominantEscalationGroup(briefing) is not null ||
               briefing.Mood.AverageMood < EscalationMoodFloor;
    }

    private static WelfareThoughtGroup? DominantEscalationGroup(WelfareSourceBriefing briefing) =>
        briefing.DataCoverage.HasMoodThoughts
            ? briefing.ThoughtDigest.ByCategory
                .Where(group => IsEscalationCategory(group.Category) && group.WorstOffset <= MaterialThoughtOffset)
                .OrderBy(group => group.WorstOffset)
                .FirstOrDefault()
            : null;

    private static bool IsEscalationCategory(ThoughtCategory category) =>
        category is not ThoughtCategory.ShelterSleep and
            not ThoughtCategory.Recreation and
            not ThoughtCategory.ComfortBeauty;

    private static string UnexplainedMoodPressureReason(WelfareSourceBriefing briefing)
    {
        WelfareThoughtGroup? group = DominantEscalationGroup(briefing);
        if (group is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"dominant_unwired_thought={ToSnakeCase(group.Category.ToString())}; pawns={group.PawnCount}; worst_offset={group.WorstOffset:0.#}; example={group.ExampleLabel}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"average_mood={briefing.Mood.AverageMood:0.##} below escalation_floor={EscalationMoodFloor:0.##}");
    }

    private static object BuildEscalationContext(WelfareSourceBriefing briefing) =>
        new WelfareEscalationContext(
            briefing.BriefingVersion,
            briefing.GameTick,
            briefing.Mood.AverageMood,
            briefing.Mood.BreakRiskCount,
            briefing.Mood.StressedCount,
            briefing.ThoughtDigest.ByCategory
                .OrderBy(group => group.WorstOffset)
                .Take(5)
                .Select(group => new WelfareEscalationThoughtGroup(
                    ToSnakeCase(group.Category.ToString()),
                    group.PawnCount,
                    group.WorstOffset,
                    group.ExampleLabel))
                .ToArray(),
            briefing.WorstPawns
                .Take(3)
                .Select(pawn => new WelfareEscalationPawn(
                    pawn.Name,
                    pawn.Mood,
                    pawn.TopNegativeThoughts
                        .Take(3)
                        .Select(thought => new WelfareEscalationThought(
                            ThoughtLabel(thought),
                            thought.MoodOffset,
                            ToSnakeCase(WelfareThoughtTaxonomy.Classify(thought.DefName, thought.Label).ToString())))
                        .ToArray()))
                .ToArray(),
            briefing.NeedLows
                .Take(5)
                .Select(need => new WelfareEscalationNeed(need.PawnName, need.Need, need.Value))
                .ToArray());

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

    private static string BreakRiskRationale(DominantDriver driver, bool hasRoutedRequest)
    {
        string category = ToSnakeCase(driver.Category.ToString());
        if (!hasRoutedRequest)
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

    private sealed record DominantDriver(
        string Label,
        ThoughtCategory Category);

    private sealed record WelfareEscalationContext(
        long BriefingVersion,
        long GameTick,
        float AverageMood,
        int BreakRiskCount,
        int StressedCount,
        IReadOnlyList<WelfareEscalationThoughtGroup> DominantThoughtGroups,
        IReadOnlyList<WelfareEscalationPawn> WorstPawns,
        IReadOnlyList<WelfareEscalationNeed> NeedLows);

    private sealed record WelfareEscalationThoughtGroup(
        string Category,
        int PawnCount,
        float WorstOffset,
        string ExampleLabel);

    private sealed record WelfareEscalationPawn(
        string Name,
        float Mood,
        IReadOnlyList<WelfareEscalationThought> TopNegativeThoughts);

    private sealed record WelfareEscalationThought(
        string Label,
        float MoodOffset,
        string Category);

    private sealed record WelfareEscalationNeed(
        string PawnName,
        string Need,
        float Value);
}
