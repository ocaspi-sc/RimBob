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
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<MinisterRule<WelfareSourceBriefing>> rules = RuleTable(now);
        IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> fallbackRuleDescriptors = FallbackRuleDescriptors();
        MinisterRuleTableResult ruleTableResult = MinisterRuleTableEvaluator.EvaluateAllHits(
            rules,
            briefing,
            additionalRuleDescriptors: fallbackRuleDescriptors);
        if (ruleTableResult.Decision is Decision decision)
            return decision;

        return new Decision(
            [],
            [],
            "needs_stable",
            DiagnosticsForFallback(rules, fallbackRuleDescriptors, briefing, "needs_stable"));
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

    private RuleEmission BreakRiskEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
    {
        AdvicePriority priority = briefing.Mood.BreakRiskCount * 2 >= briefing.ColonistCount
            ? AdvicePriority.Critical
            : AdvicePriority.High;
        WelfarePawnMood? pawn = WorstAtRiskPawn(briefing);
        string pawnName = pawn?.Name ?? "the worst-risk pawn";
        DominantDriver driver = DominantDriverFor(pawn);
        AgentFlag? routedFlag = BreakRiskDriverFlag(driver, pawnName, priority, now);

        return EmitAdvice(
            briefing,
            now,
            "break_risk",
            priority,
            $"{briefing.Mood.BreakRiskCount} colonist{Plural(briefing.Mood.BreakRiskCount)} near mental break",
            $"{pawnName} is the clearest current break-risk example; dominant driver: {driver.Label}.",
            BreakRiskRationale(driver, routedFlag),
            [new AdviceAction(AdviceActionKind.Note, $"Address {driver.Label} for {pawnName} before a mental break.", Owner: MinisterName)],
            routedFlag is null ? [] : [routedFlag]);
    }

    private RuleEmission ShelterFloorEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
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

        return EmitAdvice(
            briefing,
            now,
            "shelter_floor",
            priority,
            needsBeds ? "Colonists need more sleeping shelter" : "Sleeping room is not fully roofed",
            ShelterBody(briefing, needsBeds, bedNeed),
            ShelterRationale(briefing, needsBeds),
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, ShelterAction(briefing, needsBeds, bedNeed), Owner: "Willie")],
            [flag]);
    }

    private RuleEmission RecreationGapEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
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

        return EmitAdvice(
            briefing,
            now,
            "recreation_gap",
            priority,
            "Recreation need is falling",
            body,
            rationale,
            [new AdviceAction(canProveNoSource ? AdviceActionKind.PlaceBlueprint : AdviceActionKind.Note, RecreationAction(canProveNoSource), Owner: canProveNoSource ? "Willie" : MinisterName)],
            flags);
    }

    private RuleEmission ComfortBeautyEmission(WelfareSourceBriefing briefing, DateTimeOffset now)
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
            flags);
    }

    private static RuleEmission EmitAdvice(
        WelfareSourceBriefing briefing,
        DateTimeOffset now,
        string trace,
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

        return new RuleEmission(trace, advice, flags);
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

    private static RuleTraceDetails DiagnosticsForFallback(
        IReadOnlyList<MinisterRule<WelfareSourceBriefing>> rules,
        IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> fallbackRuleDescriptors,
        WelfareSourceBriefing briefing,
        string selectedRule) =>
        MinisterRuleTableEvaluator.BuildTrace(
            rules,
            briefing,
            selectedRule,
            emittedEmissions: [],
            additionalMatchedSignals: [new RuleTraceEntry(selectedRule, "selected", NeedsStableReason(briefing))],
            additionalRuleDescriptors: fallbackRuleDescriptors);

    private static IReadOnlyList<MinisterRuleTraceDescriptor<WelfareSourceBriefing>> FallbackRuleDescriptors() =>
    [
        new("needs_stable", NeedsStableReason, (_, selectedRule) => SelectedWhenSelected("needs_stable", selectedRule))
    ];

    private static string NeedsStableReason(WelfareSourceBriefing briefing) =>
        "no deterministic Welfare rule matched";

    private static string SelectedWhenSelected(string rule, string? selectedRule) =>
        string.Equals(rule, selectedRule, StringComparison.OrdinalIgnoreCase) ? "selected" : "not_matched";

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

    private sealed record DominantDriver(
        string Label,
        ThoughtCategory Category);
}
