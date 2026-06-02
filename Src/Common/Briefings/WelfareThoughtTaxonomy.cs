using System.Text.Json.Serialization;
using RimBob.Core.Advice;

namespace RimBob.Core.Briefings;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<ThoughtCategory>))]
public enum ThoughtCategory
{
    ShelterSleep,
    Recreation,
    ComfortBeauty,
    Hunger,
    Temperature,
    Social,
    Health,
    Ideology,
    Other
}

public static class WelfareThoughtTaxonomy
{
    private static readonly IReadOnlyDictionary<string, ThoughtCategory> CategoriesByDef =
        new Dictionary<string, ThoughtCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["SleptOutside"] = ThoughtCategory.ShelterSleep,
            ["SleptOnGround"] = ThoughtCategory.ShelterSleep,
            ["SleptInBarracks"] = ThoughtCategory.ShelterSleep,
            ["SleptInCold"] = ThoughtCategory.Temperature,
            ["SleptInHeat"] = ThoughtCategory.Temperature,
            ["Cold"] = ThoughtCategory.Temperature,
            ["Heat"] = ThoughtCategory.Temperature,
            ["SoakingWet"] = ThoughtCategory.Temperature,
            ["Bored"] = ThoughtCategory.Recreation,
            ["RecreationUnfulfilled"] = ThoughtCategory.Recreation,
            ["AteWithoutTable"] = ThoughtCategory.ComfortBeauty,
            ["AteOnFloor"] = ThoughtCategory.ComfortBeauty,
            ["Darkness"] = ThoughtCategory.ComfortBeauty,
            ["UglyEnvironment"] = ThoughtCategory.ComfortBeauty,
            ["AwfulBedroom"] = ThoughtCategory.ComfortBeauty,
            ["Hungry"] = ThoughtCategory.Hunger,
            ["RavenouslyHungry"] = ThoughtCategory.Hunger,
            ["Starving"] = ThoughtCategory.Hunger,
            ["Pain"] = ThoughtCategory.Health,
            ["Sick"] = ThoughtCategory.Health,
            ["InPain"] = ThoughtCategory.Health,
            ["Insulted"] = ThoughtCategory.Social,
            ["SocialFight"] = ThoughtCategory.Social,
            ["HarmedMe"] = ThoughtCategory.Social,
            ["IdeologyDisrespected"] = ThoughtCategory.Ideology,
            ["PreceptViolated"] = ThoughtCategory.Ideology,
            ["RitualMissed"] = ThoughtCategory.Ideology
        };

    public static ThoughtCategory Classify(string defName, string? label)
    {
        if (!string.IsNullOrWhiteSpace(defName) &&
            CategoriesByDef.TryGetValue(defName, out ThoughtCategory exact))
        {
            return exact;
        }

        string text = Normalize($"{defName} {label}");
        if (ContainsAny(text, "sleptoutside", "sleptonground", "sleptinbarracks"))
            return ThoughtCategory.ShelterSleep;
        if (ContainsAny(text, "recreation", "bored", "joy"))
            return ThoughtCategory.Recreation;
        if (ContainsAny(text, "atewithouttable", "ateonfloor", "darkness", "ugly", "beauty", "awfulbedroom"))
            return ThoughtCategory.ComfortBeauty;
        if (ContainsAny(text, "hungry", "starving", "malnutrition"))
            return ThoughtCategory.Hunger;
        if (ContainsAny(text, "cold", "heat", "hot", "soakingwet", "sleptincold", "sleptinheat"))
            return ThoughtCategory.Temperature;
        if (ContainsAny(text, "insult", "socialfight", "rival", "harmedme", "relationship"))
            return ThoughtCategory.Social;
        if (ContainsAny(text, "pain", "sick", "disease", "infection", "wound"))
            return ThoughtCategory.Health;
        if (ContainsAny(text, "ideo", "precept", "ritual"))
            return ThoughtCategory.Ideology;

        return ThoughtCategory.Other;
    }

    public static string? SuggestedOwner(ThoughtCategory category) =>
        category switch
        {
            ThoughtCategory.ShelterSleep or
            ThoughtCategory.Recreation or
            ThoughtCategory.ComfortBeauty or
            ThoughtCategory.Temperature => "Willie",
            ThoughtCategory.Hunger => "Chef",
            ThoughtCategory.Health => "Medical",
            _ => null
        };

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.Ordinal));

    private static string Normalize(string value) =>
        value.ToLowerInvariant()
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
}
