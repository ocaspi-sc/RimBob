using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Coordination;

internal static class AdviceSnapshotPolicy
{
    private const string ChefMinister = "Chef";
    private const string FoodLegacyMinister = "Food";
    private const string FoodDomain = "food";

    public static AdviceSnapshot Normalize(AdviceSnapshot snapshot)
    {
        List<AdviceItem> normalizedAdvice = snapshot.Advice
            .Select(NormalizeAdviceItem)
            .ToList();
        IReadOnlyList<AgentFlag>? normalizedFlags = snapshot.Flags?.Select(NormalizeFlag).ToList();

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

        return normalized;
    }

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

}
