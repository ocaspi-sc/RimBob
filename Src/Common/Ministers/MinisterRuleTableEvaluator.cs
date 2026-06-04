using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

public static class MinisterRuleTableEvaluator
{
    public static RuleRun EvaluateAllHits<TBriefing>(
        IReadOnlyList<MinisterRule<TBriefing>> rules,
        TBriefing briefing,
        string source = "rules",
        IReadOnlyList<MinisterRuleTraceDescriptor<TBriefing>>? additionalRuleDescriptors = null)
    {
        List<Decision> matchedDecisions = [];
        HashSet<RuleId> matchedRules = [];
        foreach (MinisterRule<TBriefing> rule in rules)
        {
            if (!rule.Matches(briefing))
                continue;

            matchedRules.Add(rule.Id);
            matchedDecisions.AddRange(rule.Build(briefing));
        }

        IReadOnlyList<Decision> decisions = Aggregate(matchedDecisions);
        RuleTraceDetails diagnostics = BuildTrace(
            rules,
            briefing,
            decisions,
            matchedRules,
            additionalRuleDescriptors);
        return new RuleRun(decisions, diagnostics);
    }

    public static IReadOnlyList<Decision> Aggregate(IReadOnlyList<Decision> decisions)
    {
        IReadOnlyList<Decision> ordered = decisions
            .OrderByDescending(DecisionPriority)
            .ThenBy(decision => decision.Rule.Value, StringComparer.Ordinal)
            .ThenBy(DecisionKindOrder)
            .ToList();

        HashSet<BuildingRequestKey> seenBuildingRequests = [];
        HashSet<LaborRequestKey> seenLaborRequests = [];
        HashSet<ItemRequestKey> seenItemRequests = [];
        HashSet<AttentionRequestKey> seenAttentionRequests = [];
        List<Decision> deduped = [];

        foreach (Decision decision in ordered)
        {
            switch (decision)
            {
                case RequestBuild request when seenBuildingRequests.Add(BuildingRequestKey.For(request.Request)):
                    deduped.Add(request);
                    break;
                case RequestLabor request when seenLaborRequests.Add(LaborRequestKey.For(request.Request)):
                    deduped.Add(request);
                    break;
                case RequestItem request when seenItemRequests.Add(ItemRequestKey.For(request.Request)):
                    deduped.Add(request);
                    break;
                case RequestAttention request when seenAttentionRequests.Add(AttentionRequestKey.For(request.Request)):
                    deduped.Add(request);
                    break;
                case Advise or Escalate:
                    deduped.Add(decision);
                    break;
            }
        }

        return deduped;
    }

    public static string CompositeTrace(IReadOnlyList<Decision> decisions)
    {
        IReadOnlyList<RuleId> rules = decisions
            .Select(decision => decision.Rule)
            .Distinct()
            .ToList();
        return rules.Count == 1
            ? rules[0].Value
            : $"rules:{string.Join("+", rules.Select(rule => rule.Value))}";
    }

    public static RuleTraceDetails BuildTrace<TBriefing>(
        IReadOnlyList<MinisterRule<TBriefing>> rules,
        TBriefing briefing,
        IReadOnlyList<Decision> decisions,
        IReadOnlySet<RuleId>? matchedRules = null,
        IReadOnlyList<MinisterRuleTraceDescriptor<TBriefing>>? additionalRuleDescriptors = null,
        RuleId? selectedRuleOverride = null)
    {
        HashSet<RuleId> emittedRules = decisions.Select(decision => decision.Rule).ToHashSet();
        HashSet<RuleId> selectedRules = matchedRules is null
            ? emittedRules
            : matchedRules.ToHashSet();
        List<RuleEvaluationTrace> allRules = [];

        foreach (MinisterRule<TBriefing> rule in rules)
        {
            string reason = rule.Reason(briefing);
            IReadOnlyList<Decision> ruleDecisions = decisions
                .Where(decision => decision.Rule == rule.Id)
                .ToList();
            RuleOutcome outcome = ruleDecisions.Any(decision => decision is Escalate)
                ? RuleOutcome.Escalated
                : selectedRules.Contains(rule.Id)
                    ? RuleOutcome.Selected
                    : RuleOutcome.NotMatched;

            allRules.Add(new RuleEvaluationTrace(
                rule.Id,
                outcome,
                reason,
                OutputActionFor(ruleDecisions),
                reason,
                EmissionsFor(ruleDecisions)));
        }

        if (additionalRuleDescriptors is not null)
        {
            RuleId? selectedRule = selectedRuleOverride ?? SelectedRule(decisions);
            foreach (MinisterRuleTraceDescriptor<TBriefing> descriptor in additionalRuleDescriptors)
            {
                string reason = descriptor.Reason(briefing);
                RuleOutcome outcome = descriptor.Outcome(briefing, selectedRule);
                allRules.Add(new RuleEvaluationTrace(
                    descriptor.Id,
                    outcome,
                    reason,
                    "",
                    reason,
                    []));
            }
        }

        return new RuleTraceDetails(selectedRuleOverride ?? SelectedRule(decisions), allRules);
    }

    private static RuleId? SelectedRule(IReadOnlyList<Decision> decisions)
    {
        IReadOnlyList<RuleId> rules = decisions
            .Select(decision => decision.Rule)
            .Distinct()
            .ToList();
        return rules.Count == 1 ? rules[0] : (RuleId?)null;
    }

    private static Priority DecisionPriority(Decision decision) =>
        decision switch
        {
            Advise advice => advice.Priority,
            RequestBuild request => request.Priority,
            RequestLabor request => request.Priority,
            RequestItem request => request.Priority,
            RequestAttention request => request.Priority,
            Escalate => Priority.Critical,
            _ => Priority.Low
        };

    private static int DecisionKindOrder(Decision decision) =>
        decision switch
        {
            Advise => 0,
            RequestBuild => 1,
            RequestLabor => 2,
            RequestItem => 3,
            RequestAttention => 4,
            Escalate => 5,
            _ => 99
        };

    private static IReadOnlyList<RuleEmission> EmissionsFor(IReadOnlyList<Decision> decisions) =>
        decisions.Select(EmissionFor).ToList();

    private static RuleEmission EmissionFor(Decision decision) =>
        decision switch
        {
            Advise advice => new RuleEmission(
                "advise",
                advice.Priority,
                advice.Title,
                null,
                null,
                null),
            RequestBuild request => new RuleEmission(
                "request_build",
                request.Priority,
                request.Request.Request,
                request.To,
                request.Request.TargetDef,
                null),
            RequestLabor request => new RuleEmission(
                "request_labor",
                request.Priority,
                request.Request.Request,
                request.To,
                null,
                request.Request.WorkType.ToString()),
            RequestItem request => new RuleEmission(
                "request_item",
                request.Priority,
                request.Request.Request,
                request.To,
                request.Request.ItemDef,
                null),
            RequestAttention request => new RuleEmission(
                "request_attention",
                request.Priority,
                request.Request.Request,
                request.To,
                null,
                null),
            Escalate escalate => new RuleEmission(
                "escalate",
                null,
                escalate.Reason,
                null,
                null,
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown rule decision type.")
        };

    private static string OutputActionFor(IReadOnlyList<Decision> decisions)
    {
        if (decisions.Count == 0)
            return "";

        List<string> parts = [];
        IReadOnlyList<string> actionKinds = decisions
            .OfType<Advise>()
            .SelectMany(advice => advice.Actions)
            .Select(action => ToSnakeCase(action.Kind.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (actionKinds.Count > 0)
            parts.Add($"actions: {string.Join(", ", actionKinds)}");

        int requestCount = decisions.Count(decision =>
            decision is RequestBuild or RequestLabor or RequestItem or RequestAttention);
        if (requestCount > 0)
            parts.Add($"requests: {requestCount}");

        if (decisions.Any(decision => decision is Escalate))
            parts.Add("escalate");

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
