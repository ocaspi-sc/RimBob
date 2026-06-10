using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;
using RimBob.State.Parsing;
using WeatherSnapshot = RimBob.Core.Briefings.WeatherSnapshot;

namespace RimBob.State.Derivations;

/// <summary>
/// Derives a MayorBriefing from ColonyState. Pure function — no side effects,
/// no I/O. Cached by BriefingCache against input aggregate versions.
/// </summary>
public static class MayorBriefingDerivation
{
    private const int PawnLineCap = 12;

    // Skills the Mayor reasons about strategically.
    private static readonly string[] StrategicSkillDefs =
        ["Plants", "Cooking", "Medicine", "Construction", "Mining", "Shooting", "Crafting", "Intellectual"];

    // "Load-bearing" trait classifications. Only listed traits surface in TraitCallouts.
    private static readonly HashSet<string> RiskTraits = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pyromaniac", "Bloodlust", "Psychopath", "ChemicalFascination",
        "ChemicalInterest", "Abrasive", "Volatile", "AnnoyingVoice", "CreepyBreathing"
    };
    private static readonly HashSet<string> StrengthTraits = new(StringComparer.OrdinalIgnoreCase)
    {
        "Industrious", "FastLearner", "Tough", "IronWilled", "Bloodlust",
        "Nimble", "Steadfast", "GreatMemory", "Hardworker", "QuickSleeper"
    };
    // Bloodlust is intentionally listed as both — it's a Welfare risk and a Defense strength.

    // Mood thresholds match RimWorld's standard tier boundaries; calibrate after first playthrough.
    private const float BreakRiskMood = 0.35f;
    private const float StressedTopMood = 0.50f;
    private const float ContentMood = 0.65f;

    public static MayorBriefing Compute(ColonyState s, long briefingVersion = 0)
    {
        GameDate date = RimDateParser.Parse(s.Economy.Value.DateTimeRaw, s.Economy.Value.Tick);
        SeasonContext season = SeasonDeriver.Derive(date);
        // Dead colonists stay in the registry until the next ingestion drops them,
        // but the briefing should reflect the living colony only.
        IReadOnlyList<ColonistRecord> pawns = PawnDeriver.LivingColonists(s.Colonists.Value.Colonists);

        var colonists = DeriveColonistsSummary(pawns);
        var skills    = DeriveSkillCoverage(pawns);
        var traits    = DeriveTraits(pawns);
        var medical   = DeriveMedical(pawns);
        FoodItemClassification foodClassification = FoodItemClassifier.Classify(
            s.Resources.Value,
            s.StoredResources.Value,
            s.Things.Value,
            s.ThingDefs.Value);
        var food      = DeriveFood(s.Farm.Value, foodClassification, pawns.Count);
        var resources = DeriveResources(s.Resources.Value, s.StoredResources.Value);
        var power     = DerivePower(s.Power.Value);
        var buildings = DeriveBuildings(s.Buildings.Value);
        var mood      = DeriveMood(pawns);
        var threat    = DeriveThreat(s.Threats.Value);
        var wealth    = DeriveWealth(s.Economy.Value, pawns.Count);
        var weather   = new WeatherSnapshot(s.Weather.Value.Def, s.Weather.Value.TemperatureC, s.Weather.Value.RainRate);
        var research  = new ResearchSnapshot(s.Research.Value.CurrentProject, s.Research.Value.Progress);

        return new MayorBriefing(
            BriefingVersion: briefingVersion,
            Date: date, GameTick: s.Economy.Value.Tick, Season: season,
            Colonists: colonists, Skills: skills, Traits: traits,
            Medical: medical, Prisoners: 0, // TODO: prisoners not yet ingested.
            Food: food, Resources: resources, Power: power, Buildings: buildings,
            Mood: mood, Threat: threat, Wealth: wealth, Weather: weather, Research: research);
    }

    // ── Derivations ───────────────────────────────────────────────────────────

    private static ColonistsSummary DeriveColonistsSummary(IReadOnlyList<ColonistRecord> pawns)
    {
        var ordered = pawns
            .OrderBy(p => p.Mood)        // worst mood first — Mayor wants the squeaky wheels
            .ThenByDescending(p => p.Age)
            .ToList();

        var lines = ordered.Take(PawnLineCap).Select(p => new PawnLine(
            Id:         p.Id,
            Name:       p.Name,
            Age:        p.Age,
            Mood:       p.Mood,
            Health:     p.Health,
            Hunger:     p.Hunger,
            IsDowned:   p.IsDowned,
            CurrentJob: p.CurrentJob,
            TopSkill:   FormatTopSkill(p.Skills)
        )).ToList();

        var extra = pawns.Count > PawnLineCap ? pawns.Count - PawnLineCap : (int?)null;

        return new ColonistsSummary(
            Count:              pawns.Count,
            Adults:             pawns.Count(p => p.Age >= 13),
            Children:           pawns.Count(p => p.Age < 13),
            Pawns:              lines,
            AdditionalNotShown: extra
        );
    }

    private static string? FormatTopSkill(IReadOnlyList<ColonistSkill> skills)
    {
        if (skills.Count == 0) return null;
        var top = skills.OrderByDescending(s => s.Level).First();
        var glyph = top.Passion switch
        {
            "Major" => "**",
            "Minor" => "*",
            _       => ""
        };
        return $"{top.Def} {top.Level}{glyph}";
    }

    private static SkillCoverage DeriveSkillCoverage(IReadOnlyList<ColonistRecord> pawns)
    {
        var byDef = new Dictionary<string, SkillCoverageEntry>();
        foreach (var def in StrategicSkillDefs)
        {
            var skills = PawnDeriver.SkillsFor(pawns, def);
            if (skills.Count == 0)
            {
                byDef[def] = new SkillCoverageEntry(0, 0, 0);
                continue;
            }
            byDef[def] = new SkillCoverageEntry(
                BestLevel:       skills.Max(s => s.Level),
                PassionatePawns: skills.Count(s => s.Passion is "Minor" or "Major"),
                QualifiedPawns:  skills.Count(s => s.Level >= 6)
            );
        }
        return new SkillCoverage(byDef);
    }

    private static TraitCallouts DeriveTraits(IReadOnlyList<ColonistRecord> pawns)
    {
        var allTraits = pawns.SelectMany(p => p.Traits).ToList();
        var risks = allTraits.Where(t => RiskTraits.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var strengths = allTraits.Where(t => StrengthTraits.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new TraitCallouts(risks, strengths);
    }

    private static MedicalState DeriveMedical(IReadOnlyList<ColonistRecord> pawns)
    {
        var downed = pawns.Count(p => p.IsDowned);
        var sick   = pawns.Count(p => !p.IsDowned && p.Health < 0.85f);
        return new MedicalState(downed, sick, 0);
        // TODO: SurgeryPending requires a surgery-bill endpoint not yet exposed.
    }

    private static FoodSnapshot DeriveFood(FarmSnapshot farm, FoodItemClassification food, int colonistCount)
    {
        // EstimatedDaysOfFood is null when we have no nutrition signal yet.
        // Item/def-backed classification wins over the summary rollup because
        // RIMAPI's total_nutrition can be stale or undercounted.
        float? daysOfFood = food.Nutrition is not null && colonistCount > 0
            ? food.Nutrition / (FoodNutrition.NutritionPerColonistPerDay * colonistCount)
            : null;
        float? latentFoodDays = colonistCount > 0
            ? food.ForbiddenEdibleNutrition / (FoodNutrition.NutritionPerColonistPerDay * colonistCount)
            : null;

        return new FoodSnapshot(
            TotalCrops:     farm.TotalCrops,
            AverageGrowth:  farm.AverageGrowth,
            ReadyToHarvest: farm.ReadyToHarvest,
            CropBreakdown:  farm.CropBreakdown
                                .Select(c => new CropBreakdown(c.Def, c.Count, c.AverageGrowth))
                                .ToList(),
            EstimatedFoodUnitsInStockpile: food.FoodUnits,
            EstimatedDaysOfFood:           daysOfFood,
            LatentFoodDays:                latentFoodDays
        );
    }

    private static ResourceSnapshot DeriveResources(ResourceSummary r, StoredResourceRegistry storedResources)
    {
        Dictionary<string, int> materials = storedResources.Items
            .Where(IsMaterialItem)
            .GroupBy(item => item.Def, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.StackCount), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> medicine = storedResources.Items
            .Where(IsMedicineItem)
            .GroupBy(item => item.Def, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.StackCount), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> weapons = storedResources.Items
            .Where(IsWeaponItem)
            .GroupBy(item => item.Def, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.StackCount), StringComparer.OrdinalIgnoreCase);

        if (medicine.Count == 0 && r.MedicineTotal > 0)
        {
            medicine = new Dictionary<string, int> { ["Medicine"] = r.MedicineTotal };
        }

        if (weapons.Count == 0 && r.WeaponCount > 0)
        {
            weapons = new Dictionary<string, int> { ["Weapons"] = r.WeaponCount };
        }

        return new ResourceSnapshot(materials, medicine, weapons);
    }

    private static bool IsMaterialItem(StoredResourceRecord item) =>
        !item.IsForbidden &&
        !IsFoodItem(item) &&
        !IsMedicineItem(item) &&
        !IsWeaponItem(item);

    private static bool IsFoodItem(StoredResourceRecord item) =>
        item.Category.Contains("food", StringComparison.OrdinalIgnoreCase) ||
        item.Def.StartsWith("Meal", StringComparison.OrdinalIgnoreCase);

    private static bool IsMedicineItem(StoredResourceRecord item) =>
        !item.IsForbidden &&
        (item.Category.Contains("medicine", StringComparison.OrdinalIgnoreCase) ||
         item.Def.Contains("Medicine", StringComparison.OrdinalIgnoreCase));

    private static bool IsWeaponItem(StoredResourceRecord item) =>
        !item.IsForbidden &&
        (item.Category.Contains("weapon", StringComparison.OrdinalIgnoreCase) ||
         item.Def.Contains("Gun", StringComparison.OrdinalIgnoreCase) ||
         item.Def.Contains("Rifle", StringComparison.OrdinalIgnoreCase) ||
         item.Def.Contains("Pistol", StringComparison.OrdinalIgnoreCase));

    private static PowerSnapshot DerivePower(PowerNetwork p) =>
        new(p.ProductionW, p.ConsumptionW, p.StoredWd, p.CapacityWd, p.ProductionW - p.ConsumptionW);

    private static BuildingsSummary DeriveBuildings(BuildingRegistry reg)
    {
        var bs = reg.Buildings;
        var strategic = new Dictionary<string, int>();

        void Tally(string label, Func<BuildingRecord, bool> pred)
        {
            var n = bs.Count(pred);
            if (n > 0) strategic[label] = n;
        }
        Tally("Beds", BuildingClassifier.IsBed);
        Tally("HospitalBeds", BuildingClassifier.IsHospitalBed);
        Tally("Batteries", BuildingClassifier.IsBattery);
        Tally("Turrets", BuildingClassifier.IsTurret);
        Tally("Generators", BuildingClassifier.IsGenerator);
        Tally("Coolers", BuildingClassifier.IsCooler);
        Tally("Heaters", BuildingClassifier.IsHeater);

        return new BuildingsSummary(
            Total:       bs.Count,
            PoweredOff:  bs.Count(b => b.PowerOn == false),
            Damaged:     bs.Count(IsDamagedBuilding),
            Strategic:   strategic
        );
    }

    private static bool IsDamagedBuilding(BuildingRecord building)
    {
        if (building.Hp is null)
            return false;

        if (building.MaxHp is > 0f)
            return building.Hp.Value / building.MaxHp.Value < 0.5f;

        return building.Hp.Value < 0.5f;
    }

    private static MoodSnapshot DeriveMood(IReadOnlyList<ColonistRecord> pawns)
    {
        if (pawns.Count == 0)
            return new MoodSnapshot(0f, 0, 0, 0);

        return new MoodSnapshot(
            AverageMood:    pawns.Average(p => p.Mood),
            BreakRiskCount: pawns.Count(p => p.Mood < BreakRiskMood),
            StressedCount:  pawns.Count(p => p.Mood is >= BreakRiskMood and < StressedTopMood),
            ContentCount:   pawns.Count(p => p.Mood > ContentMood)
        );
    }

    private static ThreatSnapshot DeriveThreat(ThreatBoard board)
    {
        // Hostile lord heuristic: any lord whose JobType names a hostile activity.
        // TODO: confirm against live RIMAPI — Lord faction-hostility may be a better signal.
        IReadOnlyList<HostileLord> hostiles = ThreatDeriver.HostileLords(board);

        return new ThreatSnapshot(
            ActiveRaid:        hostiles.Count > 0,
            HostileLordCount:  hostiles.Count,
            TotalThreatPoints: hostiles.Sum(l => l.ThreatPoints ?? 0f),
            ActiveRaids:       hostiles.Select(l => new RaidDigest(l.JobType, l.FactionId, l.ThreatPoints, l.PawnCount)).ToList(),
            RecentIncidents:   board.RecentIncidents.Select(i => $"{i.Def} ({i.DaysSince:0.#}d ago)").ToList()
        );
    }

    private static WealthSnapshot DeriveWealth(EconomyLedger e, int colonistCount)
    {
        var perColonist = colonistCount > 0 ? e.ColonyWealth / colonistCount : 0f;
        return new WealthSnapshot(e.ColonyWealth, colonistCount, perColonist);
    }
}
