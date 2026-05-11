using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State.Parsing;

namespace RimAI.State.Derivations;

public static class FoodBriefingDerivation
{
    private const int QualifiedSkillLevel = 6;

    public static FoodBriefing Compute(ColonyState s, long briefingVersion = 0)
    {
        DateStamp date = RimDateParser.Parse(s.Economy.Value.DateTimeRaw);
        SeasonContext season = DeriveSeason(date);
        IReadOnlyList<ColonistRecord> pawns = s.Colonists.Value.Colonists.Where(p => !p.IsDead).ToList();
        ResourceSummary resources = s.Resources.Value;

        float? reported = resources.TotalNutrition > 0f ? resources.TotalNutrition : null;
        float fallback = FoodNutrition.EstimateFallback(resources.MealsCount, resources.RawFoodCount);
        float? fallbackNutrition = fallback > 0f ? fallback : null;
        float? nutrition = reported ?? fallbackNutrition;
        string nutritionSource = reported is not null
            ? "reported"
            : fallbackNutrition is not null
                ? "fallback_meal_raw_counts"
                : "unknown";
        float? days = nutrition is not null && pawns.Count > 0
            ? nutrition / (FoodNutrition.NutritionPerColonistPerDay * pawns.Count)
            : null;

        IReadOnlyList<string> incidents = s.Threats.Value.RecentIncidents
            .Where(i => IsFoodIncident(i.Def) || (i.Label is not null && IsFoodIncident(i.Label)))
            .Select(i => i.Label is null ? $"{i.Def} ({i.DaysSince:0.#}d ago)" : $"{i.Label} ({i.DaysSince:0.#}d ago)")
            .ToList();

        return new FoodBriefing(
            BriefingVersion: briefingVersion,
            Date: date,
            GameTick: s.Economy.Value.Tick,
            Season: season,
            ColonistCount: pawns.Count,
            ReportedNutrition: reported,
            FallbackNutrition: fallbackNutrition,
            NutritionSource: nutritionSource,
            EstimatedDaysOfFood: days,
            FoodUnits: resources.FoodTotal,
            MealsCount: resources.MealsCount,
            RawFoodCount: resources.RawFoodCount,
            ReadyToHarvest: s.Farm.Value.ReadyToHarvest,
            CropBreakdown: s.Farm.Value.CropBreakdown
                .Select(c => new FoodCropSummary(c.Def, c.Count, c.AverageGrowth))
                .ToList(),
            WildHarvestCandidates: s.Plants.Value.Plants.Count(p => !p.IsCrop && p.Growth >= 0.85f),
            WildAnimalCount: s.Animals.Value.Animals.Count(a => !a.Tame && a.Health > 0.6f),
            StockpileCells: s.Stockpiles.Value.Zones.Sum(z => z.CellCount),
            Skills: DeriveSkills(pawns),
            Infrastructure: DeriveInfrastructure(s),
            ActiveThreat: DeriveActiveThreat(s.Threats.Value),
            RecentFoodIncidents: incidents
        );
    }

    private static FoodSkillSnapshot DeriveSkills(IReadOnlyList<ColonistRecord> pawns) =>
        new(BestSkill(pawns, "Plants"), QualifiedSkillCount(pawns, "Plants"),
            BestSkill(pawns, "Cooking"), QualifiedSkillCount(pawns, "Cooking"));

    private static int BestSkill(IReadOnlyList<ColonistRecord> pawns, string def) =>
        pawns.SelectMany(p => p.Skills)
            .Where(s => s.Def.Equals(def, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Level)
            .DefaultIfEmpty(0)
            .Max();

    private static int QualifiedSkillCount(IReadOnlyList<ColonistRecord> pawns, string def) =>
        pawns.Count(p => p.Skills.Any(s =>
            s.Def.Equals(def, StringComparison.OrdinalIgnoreCase) && s.Level >= QualifiedSkillLevel));

    private static FoodInfrastructureSnapshot DeriveInfrastructure(ColonyState s)
    {
        int coolers = s.Buildings.Value.Buildings.Count(b =>
            b.Def.Contains("Cooler", StringComparison.OrdinalIgnoreCase));
        float netPower = s.Power.Value.ProductionW - s.Power.Value.ConsumptionW;
        int foodStockpileZones = s.Stockpiles.Value.Zones.Count(z =>
            (z.Label ?? z.Type).Contains("food", StringComparison.OrdinalIgnoreCase) ||
            z.Type.Contains("Stockpile", StringComparison.OrdinalIgnoreCase));
        return new FoodInfrastructureSnapshot(coolers, netPower >= 0, netPower, foodStockpileZones);
    }

    private static bool DeriveActiveThreat(ThreatBoard board) =>
        board.Lords.Any(l => l.JobType is not null && (
            l.JobType.Contains("Raid", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Siege", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Assault", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Sapper", StringComparison.OrdinalIgnoreCase)));

    private static bool IsFoodIncident(string text) =>
        text.Contains("Blight", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ColdSnap", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Food", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ToxicFallout", StringComparison.OrdinalIgnoreCase);

    private static SeasonContext DeriveSeason(DateStamp date)
    {
        string[] quadrums = ["Aprimay", "Jugust", "Septober", "Decembary"];
        const int daysPerQuadrum = 15;
        if (date.Quadrum is null || date.Day is null)
            return new SeasonContext(null, null, null);

        int idx = Array.FindIndex(quadrums, q => q.Equals(date.Quadrum, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return new SeasonContext(date.Quadrum, null, null);

        int daysToNext = daysPerQuadrum - date.Day.Value + 1;
        int winterIdx = Array.IndexOf(quadrums, "Decembary");
        int quadrumsAway = (winterIdx - idx + 4) % 4;
        int daysToWinter = quadrumsAway == 0
            ? 0
            : (quadrumsAway - 1) * daysPerQuadrum + daysToNext;

        return new SeasonContext(quadrums[idx], daysToNext, daysToWinter);
    }
}
