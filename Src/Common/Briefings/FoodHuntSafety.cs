using RimBob.Core.Aggregates;

namespace RimBob.Core.Briefings;

public static class FoodHuntSafety
{
    public const float MinimumHealthyWildHealth = 0.6f;
    public const float DangerousManhunterChance = 0.2f;
    public const float CautionManhunterChance = 0.05f;
    public const float HerdPackCautionManhunterChance = 0.1f;
    public const float DangerousBodySize = 2.0f;
    public const float CautionBodySize = 1.5f;

    public static bool IsHealthyWildAnimal(AnimalRecord animal) =>
        !animal.Tame && !animal.Bonded && animal.Health > MinimumHealthyWildHealth;

    public static bool IsLowRiskTarget(AnimalRecord animal) =>
        IsHealthyWildAnimal(animal) && !IsDangerousHuntDef(animal.Def);

    public static bool IsLowRiskTarget(AnimalRecord animal, AnimalDefRegistry animalDefs) =>
        Assess(animal, animalDefs).IsLowRisk;

    public static HuntRiskDiagnostic Diagnose(AnimalRecord animal, AnimalDefRegistry animalDefs)
    {
        List<HuntRiskSignal> signals = [];
        if (!IsHealthyWildAnimal(animal))
        {
            string reason = EligibilityReason(animal);
            signals.Add(new HuntRiskSignal("eligibility", "Eligibility", reason, "blocked"));
            return new HuntRiskDiagnostic(
                animal.Id,
                animal.Def,
                IsHealthyWild: false,
                MetadataAvailable: false,
                FallbackUsed: false,
                new HuntRiskProfile(animal.Def, "blocked", 4, false, null, reason),
                signals);
        }

        signals.Add(new HuntRiskSignal("eligibility", "Eligibility", "healthy wild animal", "low"));

        animalDefs.DefsByName.TryGetValue(animal.Def, out AnimalDefRecord? def);
        if (def is null)
        {
            bool dangerousByName = IsDangerousHuntDef(animal.Def);
            int rank = dangerousByName ? 3 : RiskRank(animal.Def);
            string reason = dangerousByName ? "dangerous def-name match" : "safe by fallback def-name check";
            signals.Add(new HuntRiskSignal("metadata", "Animal def metadata", "missing", "caution"));
            signals.Add(new HuntRiskSignal("fallback", "Fallback", reason, dangerousByName ? "dangerous" : "low"));
            return new HuntRiskDiagnostic(
                animal.Id,
                animal.Def,
                IsHealthyWild: true,
                MetadataAvailable: false,
                FallbackUsed: true,
                new HuntRiskProfile(animal.Def, dangerousByName ? "dangerous" : "low", rank, !dangerousByName, null, reason),
                signals);
        }

        signals.Add(new HuntRiskSignal("metadata", "Animal def metadata", "available", "neutral"));

        List<string> riskReasons = [];
        AddDangerSignal(signals, riskReasons, def.Explosive, "explosive", "Explosive");
        AddDangerSignal(signals, riskReasons, def.Predator, "predator", "Predator");
        AddDangerSignal(signals, riskReasons, def.IsInsect, "insect", "Insect");
        if (def.ManhunterOnDamageChance >= DangerousManhunterChance)
        {
            string reason = $"high revenge chance {def.ManhunterOnDamageChance:P0}";
            riskReasons.Add(reason);
            signals.Add(new HuntRiskSignal("manhunter_on_damage_chance", "Revenge chance", reason, "dangerous"));
        }
        if (def.BodySize >= DangerousBodySize)
        {
            string reason = $"large body size {def.BodySize:0.0}";
            riskReasons.Add(reason);
            signals.Add(new HuntRiskSignal("body_size", "Body size", reason, "dangerous"));
        }
        if (IsDangerousHuntDef(animal.Def))
        {
            riskReasons.Add("dangerous def-name match");
            signals.Add(new HuntRiskSignal("def_name", "Def-name guard", "dangerous def-name match", "dangerous"));
        }

        float? nutrition = NutritionOrNull(def.EstimatedMeatNutrition);
        if (nutrition is not null)
            signals.Add(new HuntRiskSignal("estimated_meat_nutrition", "Estimated nutrition", $"{nutrition.Value:0.#} each", "neutral"));

        if (riskReasons.Count > 0)
        {
            return new HuntRiskDiagnostic(
                animal.Id,
                animal.Def,
                IsHealthyWild: true,
                MetadataAvailable: true,
                FallbackUsed: false,
                new HuntRiskProfile(animal.Def, "dangerous", 3, false, nutrition, string.Join("; ", riskReasons)),
                signals);
        }

        List<string> cautionReasons = [];
        if (def.ManhunterOnDamageChance >= CautionManhunterChance)
        {
            string reason = $"revenge chance {def.ManhunterOnDamageChance:P0}";
            cautionReasons.Add(reason);
            signals.Add(new HuntRiskSignal("manhunter_on_damage_chance", "Revenge chance", reason, "caution"));
        }
        if ((def.HerdAnimal || def.PackAnimal) && def.ManhunterOnDamageChance >= HerdPackCautionManhunterChance)
        {
            string reason = def.HerdAnimal ? "herd animal" : "pack animal";
            cautionReasons.Add(reason);
            signals.Add(new HuntRiskSignal("group_behavior", "Group behavior", reason, "caution"));
        }
        if (def.BodySize >= CautionBodySize)
        {
            string reason = $"heavy body size {def.BodySize:0.0}";
            cautionReasons.Add(reason);
            signals.Add(new HuntRiskSignal("body_size", "Body size", reason, "caution"));
        }

        if (cautionReasons.Count > 0)
        {
            return new HuntRiskDiagnostic(
                animal.Id,
                animal.Def,
                IsHealthyWild: true,
                MetadataAvailable: true,
                FallbackUsed: false,
                new HuntRiskProfile(animal.Def, "caution", 2, false, nutrition, string.Join("; ", cautionReasons)),
                signals);
        }

        string value = def.EstimatedMeatNutrition > 0f
            ? $"about {def.EstimatedMeatNutrition:0.#} nutrition each"
            : "low-risk metadata";
        signals.Add(new HuntRiskSignal("risk", "Risk result", "low-risk metadata", "low"));
        return new HuntRiskDiagnostic(
            animal.Id,
            animal.Def,
            IsHealthyWild: true,
            MetadataAvailable: true,
            FallbackUsed: false,
            new HuntRiskProfile(animal.Def, "low", 0, true, nutrition, value),
            signals);
    }

    public static HuntRiskProfile Assess(AnimalRecord animal, AnimalDefRegistry animalDefs)
    {
        return Diagnose(animal, animalDefs).Profile;
    }

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

    private static float? NutritionOrNull(float nutrition) =>
        nutrition > 0f ? nutrition : null;

    private static string EligibilityReason(AnimalRecord animal)
    {
        if (animal.Tame)
            return "tame animal";
        if (animal.Bonded)
            return "bonded animal";
        if (animal.Health <= MinimumHealthyWildHealth)
            return $"health {animal.Health:P0}";
        return "not a healthy wild animal";
    }

    private static void AddDangerSignal(
        List<HuntRiskSignal> signals,
        List<string> reasons,
        bool active,
        string key,
        string label)
    {
        if (!active)
            return;

        reasons.Add(key);
        signals.Add(new HuntRiskSignal(key, label, key, "dangerous"));
    }
}

public sealed record HuntRiskDiagnostic(
    string AnimalId,
    string Def,
    bool IsHealthyWild,
    bool MetadataAvailable,
    bool FallbackUsed,
    HuntRiskProfile Profile,
    IReadOnlyList<HuntRiskSignal> Signals
);

public sealed record HuntRiskSignal(
    string Key,
    string Label,
    string Value,
    string Tone
);

public sealed record HuntRiskProfile(
    string Def,
    string Risk,
    int RiskRank,
    bool IsLowRisk,
    float? EstimatedNutrition,
    string? Reason
);
