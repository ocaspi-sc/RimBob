using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Coordination;

internal static class AdviceSnapshotPolicy
{
    private const string ChefMinister = "Chef";
    private const string FoodLegacyMinister = "Food";
    private const string FoodDomain = "food";
    private const string MigratedCookLaborFlagId = "food:migrated_cook_labor_request";

    public static AdviceSnapshot Normalize(AdviceSnapshot snapshot)
    {
        List<AdviceItem> normalizedAdvice = snapshot.Advice
            .Select(NormalizeAdviceItem)
            .ToList();
        IReadOnlyList<AgentFlag>? normalizedFlags = NormalizeFlags(
            snapshot.Flags?.Select(NormalizeFlag).ToList(),
            snapshot.Advice);

        return snapshot with
        {
            Minister = NormalizeMinisterName(snapshot.Minister),
            Advice = normalizedAdvice,
            StateSummaries = NormalizeMinisterMap(snapshot.StateSummaries),
            Chains = NormalizeMinisterMap(snapshot.Chains),
            Flags = normalizedFlags
        };
    }

    public static AdviceItem NormalizeAdviceItem(AdviceItem item)
    {
        AdviceItem normalized = item;
        if (IsFoodMinisterAlias(item.Minister))
            normalized = normalized with { Minister = ChefMinister };
        if (normalized.BriefingRef is not null && IsFoodMinisterAlias(normalized.BriefingRef.Minister))
            normalized = normalized with { BriefingRef = normalized.BriefingRef with { Minister = ChefMinister } };

        List<AdviceAction> actions = normalized.Actions
            .Where(action => !IsObsoleteFoodCookPriorityAction(normalized, action))
            .ToList();

        return actions.Count == normalized.Actions.Count
            ? normalized
            : normalized with { Actions = actions };
    }

    private static IReadOnlyList<AgentFlag>? NormalizeFlags(
        IReadOnlyList<AgentFlag>? flags,
        IReadOnlyList<AdviceItem> originalAdvice)
    {
        List<AgentFlag> normalizedFlags = flags?.ToList() ?? [];
        if (!ContainsObsoleteFoodCookPriorityAction(originalAdvice) || HasFoodCookLaborRequest(normalizedFlags))
            return flags;

        AdviceItem sourceAdvice = originalAdvice.First(item =>
            item.Actions.Any(action => IsObsoleteFoodCookPriorityAction(item, action)));
        AdviceAction sourceAction = sourceAdvice.Actions.First(action =>
            IsObsoleteFoodCookPriorityAction(sourceAdvice, action));

        // WHY: persisted snapshots can outlive rule changes; keep routing pressure visible
        // without reintroducing an ungrounded player-facing priority action.
        normalizedFlags.Add(new AgentFlag(
            Id: MigratedCookLaborFlagId,
            SourceMinister: ChefMinister,
            Severity: SeverityFor(sourceAdvice.Priority),
            Domain: FoodDomain,
            Summary: "Chef needs Cook labor",
            LaborRequests:
            [
                new LaborRequest(
                    Request: "Cook work today",
                    Reason: "raw food has to become meals before it solves food pressure",
                    WorkType: WorkType.Cook,
                    Skill: sourceAction.Skill ?? "Cooking",
                    Priority: sourceAdvice.Priority,
                    RequestedFrom: sourceAction.Owner ?? "Labor")
            ],
            Detail: "Migrated from obsolete Chef Cook priority advice action.",
            ExpiresAt: sourceAdvice.ExpiresAt));

        return normalizedFlags;
    }

    private static bool ContainsObsoleteFoodCookPriorityAction(IReadOnlyList<AdviceItem> advice) =>
        advice.Any(item => item.Actions.Any(action => IsObsoleteFoodCookPriorityAction(item, action)));

    private static bool IsObsoleteFoodCookPriorityAction(AdviceItem item, AdviceAction action)
    {
        if (!IsFoodMinisterAlias(item.Minister))
            return false;

        return action.Kind == AdviceActionKind.SetPriority &&
               (action.WorkType == WorkType.Cook ||
                action.Instruction.Contains("Cook", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFoodCookLaborRequest(IReadOnlyList<AgentFlag> flags) =>
        flags.Any(flag =>
            IsFoodMinisterAlias(flag.SourceMinister) &&
            flag.LaborRequests is not null &&
            flag.LaborRequests.Any(request => request.WorkType == WorkType.Cook));

    private static AgentFlag NormalizeFlag(AgentFlag flag) =>
        IsFoodMinisterAlias(flag.SourceMinister)
            ? flag with { SourceMinister = ChefMinister }
            : flag;

    private static IReadOnlyDictionary<string, T>? NormalizeMinisterMap<T>(IReadOnlyDictionary<string, T>? values)
    {
        if (values is null) return null;

        Dictionary<string, T> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, T> pair in values)
            normalized[NormalizeRequiredMinisterName(pair.Key)] = pair.Value;

        return normalized;
    }

    private static string? NormalizeMinisterName(string? minister) =>
        minister is null ? null : NormalizeRequiredMinisterName(minister);

    private static string NormalizeRequiredMinisterName(string minister) =>
        IsFoodMinisterAlias(minister) ? ChefMinister : minister;

    private static bool IsFoodMinisterAlias(string minister)
    {
        string normalized = MinisterRegistry.NormalizeKey(minister);
        return normalized.Equals(FoodDomain, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(MinisterRegistry.NormalizeKey(FoodLegacyMinister), StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(MinisterRegistry.NormalizeKey(ChefMinister), StringComparison.OrdinalIgnoreCase);
    }

    private static FlagSeverity SeverityFor(AdvicePriority priority) =>
        priority switch
        {
            AdvicePriority.Critical => FlagSeverity.Critical,
            AdvicePriority.High => FlagSeverity.High,
            AdvicePriority.Medium => FlagSeverity.Medium,
            _ => FlagSeverity.Low
        };
}
