using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Coordination;

internal static class AdviceSnapshotPolicy
{
    private const string FoodMinister = "Food";
    private const string FoodDomain = "food";
    private const string MigratedCookLaborFlagId = "food:migrated_cook_labor_request";

    public static AdviceSnapshot Normalize(AdviceSnapshot snapshot)
    {
        List<AdviceItem> originalAdvice = snapshot.Advice.ToList();
        List<AdviceItem> normalizedAdvice = originalAdvice
            .Select(NormalizeAdviceItem)
            .ToList();
        IReadOnlyList<AgentFlag>? normalizedFlags = NormalizeFlags(snapshot.Flags, originalAdvice);

        return snapshot with
        {
            Advice = normalizedAdvice,
            Flags = normalizedFlags
        };
    }

    public static AdviceItem NormalizeAdviceItem(AdviceItem item)
    {
        List<AdviceAction> actions = item.Actions
            .Where(action => !IsObsoleteFoodCookPriorityAction(item, action))
            .ToList();

        return actions.Count == item.Actions.Count
            ? item
            : item with { Actions = actions };
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
            SourceMinister: FoodMinister,
            Severity: SeverityFor(sourceAdvice.Priority),
            Domain: FoodDomain,
            Summary: "Food needs Cook labor",
            Requests:
            [
                new ResourceRequest(
                    ResourceRequestKind.Labor,
                    "Cook work today",
                    sourceAction.Reason ?? "raw food has to become meals before it solves food pressure",
                    Priority: sourceAdvice.Priority,
                    RequestedFrom: sourceAction.Owner ?? "Labor",
                    WorkType: WorkType.Cook,
                    Skill: sourceAction.Skill ?? "Cooking",
                    Icon: sourceAction.Icon)
            ],
            Detail: "Migrated from obsolete Food Cook priority advice action.",
            ExpiresAt: sourceAdvice.ExpiresAt));

        return normalizedFlags;
    }

    private static bool ContainsObsoleteFoodCookPriorityAction(IReadOnlyList<AdviceItem> advice) =>
        advice.Any(item => item.Actions.Any(action => IsObsoleteFoodCookPriorityAction(item, action)));

    private static bool IsObsoleteFoodCookPriorityAction(AdviceItem item, AdviceAction action)
    {
        if (!string.Equals(item.Minister, FoodMinister, StringComparison.OrdinalIgnoreCase))
            return false;

        return action.Kind == AdviceActionKind.SetPriority &&
               (action.WorkType == WorkType.Cook ||
                action.Instruction.Contains("Cook", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFoodCookLaborRequest(IReadOnlyList<AgentFlag> flags) =>
        flags.Any(flag =>
            string.Equals(flag.SourceMinister, FoodMinister, StringComparison.OrdinalIgnoreCase) &&
            flag.Requests is not null &&
            flag.Requests.Any(request =>
                request.Kind == ResourceRequestKind.Labor &&
                request.WorkType == WorkType.Cook));

    private static FlagSeverity SeverityFor(AdvicePriority priority) =>
        priority switch
        {
            AdvicePriority.Critical => FlagSeverity.Critical,
            AdvicePriority.High => FlagSeverity.High,
            AdvicePriority.Medium => FlagSeverity.Medium,
            _ => FlagSeverity.Low
        };
}
