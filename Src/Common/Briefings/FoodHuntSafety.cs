using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public static class FoodHuntSafety
{
    public static bool IsHealthyWildAnimal(AnimalRecord animal) =>
        !animal.Tame && animal.Health > 0.6f;

    public static bool IsLowRiskTarget(AnimalRecord animal) =>
        IsHealthyWildAnimal(animal) && !IsDangerousHuntDef(animal.Def);

    public static bool IsDangerousHuntDef(string def)
    {
        string normalized = def.ToLowerInvariant();
        return ContainsAny(normalized,
        [
            "bear",
            "boom",
            "cobra",
            "cougar",
            "elephant",
            "insect",
            "lynx",
            "mega",
            "panther",
            "rhinoceros",
            "scaria",
            "thrumbo",
            "warg",
            "wolf"
        ]);
    }

    public static int RiskRank(string def)
    {
        string normalized = def.ToLowerInvariant();
        if (ContainsAny(normalized, ["hare", "rabbit", "squirrel", "rat", "turkey", "tortoise"]))
            return 0;

        if (ContainsAny(normalized, ["deer", "doe", "buck", "gazelle", "ibex", "elk", "caribou", "alpaca", "dromedary"]))
            return 1;

        return 2;
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
