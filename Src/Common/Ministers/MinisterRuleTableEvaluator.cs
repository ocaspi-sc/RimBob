using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

public static class MinisterRuleTableEvaluator
{
    public static MinisterRuleTableResult EvaluateAllHits<TBriefing>(
        IReadOnlyList<MinisterRule<TBriefing>> rules,
        TBriefing briefing,
        string source = "rules",
        IReadOnlyList<MinisterRuleTraceDescriptor<TBriefing>>? additionalRuleDescriptors = null)
    {
        List<RuleEmission> matchedEmissions = [];
        foreach (MinisterRule<TBriefing> rule in rules)
        {
            if (rule.Matches(briefing))
                matchedEmissions.Add(rule.Build(briefing));
        }

        if (matchedEmissions.Count == 0)
        {
            RuleTraceDetails emptyDiagnostics = BuildTrace(
                rules,
                briefing,
                selectedRule: null,
                emittedEmissions: [],
                source: source,
                additionalRuleDescriptors: additionalRuleDescriptors);
            return new MinisterRuleTableResult(false, null, emptyDiagnostics);
        }

        MinisterRuleAggregate aggregate = Aggregate(matchedEmissions);
        RuleTraceDetails diagnostics = BuildTrace(
            rules,
            briefing,
            aggregate.Trace,
            aggregate.DedupedEmissions,
            source,
            additionalRuleDescriptors: additionalRuleDescriptors);
        Decision decision = new(aggregate.Advice, aggregate.Flags, aggregate.Trace, diagnostics);
        return new MinisterRuleTableResult(true, decision, diagnostics);
    }

    public static MinisterRuleAggregate Aggregate(IReadOnlyList<RuleEmission> emissions)
    {
        IReadOnlyList<RuleEmission> orderedEmissions = emissions
            .OrderByDescending(emission => emission.Advice.Priority)
            .ThenBy(emission => emission.Rule, StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<RuleEmission> dedupedEmissions = DeduplicateFlagRequests(orderedEmissions)
            .ToList();
        IReadOnlyList<AdviceItem> advice = orderedEmissions
            .Select(emission => emission.Advice)
            .ToList();
        IReadOnlyList<AgentFlag> flags = dedupedEmissions
            .SelectMany(emission => emission.Flags)
            .ToList();
        string trace = CompositeTrace(orderedEmissions);
        return new MinisterRuleAggregate(orderedEmissions, dedupedEmissions, advice, flags, trace);
    }

    public static string CompositeTrace(IReadOnlyList<RuleEmission> emissions) =>
        emissions.Count == 1
            ? emissions[0].Rule
            : $"rules:{string.Join("+", emissions.Select(emission => emission.Rule))}";

    public static RuleTraceDetails BuildTrace<TBriefing>(
        IReadOnlyList<MinisterRule<TBriefing>> rules,
        TBriefing briefing,
        string? selectedRule,
        IReadOnlyList<RuleEmission> emittedEmissions,
        string source = "rules",
        IReadOnlyList<RuleTraceEntry>? additionalMatchedSignals = null,
        IReadOnlyList<MinisterRuleTraceDescriptor<TBriefing>>? additionalRuleDescriptors = null)
    {
        Dictionary<string, RuleEmission> emittedByRule = emittedEmissions.ToDictionary(
            emission => emission.Rule,
            StringComparer.OrdinalIgnoreCase);
        List<RuleTraceEntry> matchedSignals = [];
        List<RuleEvaluationTrace> allRules = [];

        foreach (MinisterRule<TBriefing> rule in rules)
        {
            bool matched = rule.Matches(briefing);
            string reason = rule.Reason(briefing);
            if (matched)
                matchedSignals.Add(new RuleTraceEntry(rule.Id, "selected", reason));

            bool emitted = emittedByRule.TryGetValue(rule.Id, out RuleEmission? emission);
            allRules.Add(new RuleEvaluationTrace(
                rule.Id,
                emitted ? "selected" : "not_matched",
                reason,
                OutputActionFor(emission),
                emitted ? reason : null));
        }

        if (additionalMatchedSignals is not null)
            matchedSignals.AddRange(additionalMatchedSignals);

        if (additionalRuleDescriptors is not null)
        {
            foreach (MinisterRuleTraceDescriptor<TBriefing> descriptor in additionalRuleDescriptors)
            {
                string reason = descriptor.Reason(briefing);
                string outcome = descriptor.Outcome(briefing, selectedRule);
                allRules.Add(new RuleEvaluationTrace(
                    descriptor.Id,
                    outcome,
                    reason,
                    "",
                    outcome == "not_matched" ? null : reason));
            }
        }

        RuleTraceDetails details = new(selectedRule, matchedSignals, [])
        {
            AllRules = allRules
        };
        return WithRuleEmissions(details, emittedEmissions, source);
    }

    private static IReadOnlyList<RuleEmission> DeduplicateFlagRequests(IReadOnlyList<RuleEmission> orderedEmissions)
    {
        HashSet<BuildingRequestKey> seenBuildingRequests = [];
        HashSet<LaborRequestKey> seenLaborRequests = [];
        HashSet<ItemRequestKey> seenItemRequests = [];
        HashSet<AttentionRequestKey> seenAttentionRequests = [];
        List<RuleEmission> dedupedEmissions = [];

        foreach (RuleEmission emission in orderedEmissions)
        {
            IReadOnlyList<AgentFlag> dedupedFlags = emission.Flags
                .Select(flag => DeduplicateFlagRequests(
                    flag,
                    seenBuildingRequests,
                    seenLaborRequests,
                    seenItemRequests,
                    seenAttentionRequests))
                .ToList();
            dedupedEmissions.Add(emission with { Flags = dedupedFlags });
        }

        return dedupedEmissions;
    }

    private static AgentFlag DeduplicateFlagRequests(
        AgentFlag flag,
        HashSet<BuildingRequestKey> seenBuildingRequests,
        HashSet<LaborRequestKey> seenLaborRequests,
        HashSet<ItemRequestKey> seenItemRequests,
        HashSet<AttentionRequestKey> seenAttentionRequests)
    {
        IReadOnlyList<BuildingRequest> buildingRequests = FilterUnseenRequests(
            flag.BuildingRequests,
            seenBuildingRequests,
            BuildingRequestKey.For);
        IReadOnlyList<LaborRequest> laborRequests = FilterUnseenRequests(
            flag.LaborRequests,
            seenLaborRequests,
            LaborRequestKey.For);
        IReadOnlyList<ItemRequest> itemRequests = FilterUnseenRequests(
            flag.ItemRequests,
            seenItemRequests,
            ItemRequestKey.For);
        IReadOnlyList<AttentionRequest> attentionRequests = FilterUnseenRequests(
            flag.Attention,
            seenAttentionRequests,
            AttentionRequestKey.For);

        return flag with
        {
            BuildingRequests = NullIfEmpty(buildingRequests),
            LaborRequests = NullIfEmpty(laborRequests),
            ItemRequests = NullIfEmpty(itemRequests),
            Attention = NullIfEmpty(attentionRequests)
        };
    }

    private static IReadOnlyList<TRequest> FilterUnseenRequests<TRequest, TKey>(
        IReadOnlyList<TRequest>? requests,
        HashSet<TKey> seenRequests,
        Func<TRequest, TKey> keyFor)
        where TKey : notnull
    {
        if (requests is null || requests.Count == 0)
            return [];

        List<TRequest> filtered = [];
        foreach (TRequest request in requests)
        {
            if (seenRequests.Add(keyFor(request)))
                filtered.Add(request);
        }

        return filtered;
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> requests) =>
        requests.Count == 0 ? null : requests;

    private static RuleTraceDetails WithRuleEmissions(
        RuleTraceDetails details,
        IReadOnlyList<RuleEmission> emissions,
        string source)
    {
        List<RuleEmittedAdviceTrace> emittedAdvice = [];
        List<RuleEmittedActionTrace> emittedActions = [];
        List<RuleEmittedFlagTrace> emittedFlags = [];

        foreach (RuleEmission emission in emissions)
        {
            AdviceItem item = emission.Advice;
            emittedAdvice.Add(new RuleEmittedAdviceTrace(
                Source: source,
                Rule: emission.Rule,
                AdviceId: item.Id,
                Priority: item.Priority,
                Title: item.Title,
                ActionCount: item.Actions.Count));

            for (int i = 0; i < item.Actions.Count; i++)
            {
                AdviceAction action = item.Actions[i];
                emittedActions.Add(new RuleEmittedActionTrace(
                    Source: source,
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
                    Source: source,
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

    private static string OutputActionFor(RuleEmission? emission)
    {
        if (emission is null)
            return "";

        List<string> parts = [];
        IReadOnlyList<string> actionKinds = emission.Advice.Actions
            .Select(action => ToSnakeCase(action.Kind.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (actionKinds.Count > 0)
            parts.Add($"actions: {string.Join(", ", actionKinds)}");

        int flagCount = emission.Flags.Count;
        int requestCount = emission.Flags.Sum(flag =>
            (flag.BuildingRequests?.Count ?? 0) +
            (flag.LaborRequests?.Count ?? 0) +
            (flag.ItemRequests?.Count ?? 0) +
            (flag.Attention?.Count ?? 0));
        if (flagCount > 0)
            parts.Add(requestCount > 0 ? $"flags: {flagCount}; requests: {requestCount}" : $"flags: {flagCount}");

        return string.Join("; ", parts);
    }

    private static string ToSnakeCase(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (char.IsUpper(current) && i > 0)
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(current));
        }

        return new string(chars.ToArray());
    }

    private readonly record struct BuildingRequestKey(BuildingClass TargetClass, RoomClass? RoomClass, string? TargetDef)
    {
        public static BuildingRequestKey For(BuildingRequest request) =>
            new(request.TargetClass, request.RoomClass, NormalizeKeyText(request.TargetDef));
    }

    private readonly record struct LaborRequestKey(WorkType WorkType, string? Skill)
    {
        public static LaborRequestKey For(LaborRequest request) =>
            new(request.WorkType, NormalizeKeyText(request.Skill));
    }

    private readonly record struct ItemRequestKey(string? ItemDef)
    {
        public static ItemRequestKey For(ItemRequest request) =>
            new(NormalizeKeyText(request.ItemDef));
    }

    private readonly record struct AttentionRequestKey(string Request, string? RequestedFrom)
    {
        public static AttentionRequestKey For(AttentionRequest request) =>
            new(NormalizeKeyText(request.Request) ?? "", NormalizeKeyText(request.RequestedFrom));
    }

    private static string? NormalizeKeyText(string? value) =>
        value is null ? null : value.ToUpperInvariant();
}
