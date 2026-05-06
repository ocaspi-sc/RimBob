using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State.Parsing;
using WeatherSnapshot = RimAI.Core.Briefings.WeatherSnapshot;
using AggWeather      = RimAI.Core.Aggregates.WeatherSnapshot;

namespace RimAI.State.Derivations;

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

    // Quadrum order in temperate biomes; winter = Decembary.
    private static readonly string[] Quadrums = ["Aprimay", "Jugust", "Septober", "Decembary"];
    private const int DaysPerQuadrum = 15;

    public static MayorBriefing Compute(ColonyState s, long briefingVersion = 0)
    {
        var date     = RimDateParser.Parse(s.Economy.Value.DateTimeRaw);
        var season   = DeriveSeason(date);
        var pawns    = s.Colonists.Value.Colonists;

        var colonists = DeriveColonistsSummary(pawns);
        var skills    = DeriveSkillCoverage(pawns);
        var traits    = DeriveTraits(pawns);
        var medical   = DeriveMedical(pawns);
        var food      = DeriveFood(s.Farm.Value, s.Stockpiles.Value, pawns.Count);
        var resources = DeriveResources(s.Stockpiles.Value);
        var power     = DerivePower(s.Power.Value);
        var buildings = DeriveBuildings(s.Buildings.Value);
        var mood      = DeriveMood(pawns);
        var threat    = DeriveThreat(s.Threats.Value);
        var wealth    = DeriveWealth(s.Economy.Value, pawns.Count);
        var weather   = new WeatherSnapshot(s.Weather.Value.Def, s.Weather.Value.TemperatureC, s.Weather.Value.RainRate);
        var research  = new ResearchSnapshot(null, null);
        // TODO: research endpoint not yet exposed in RimApiClient.

        return new MayorBriefing(
            BriefingVersion: briefingVersion,
            Date: date, GameTick: s.Economy.Value.Tick, Season: season,
            Colonists: colonists, Skills: skills, Traits: traits,
            Medical: medical, Prisoners: 0, // TODO: prisoners not yet ingested.
            Food: food, Resources: resources, Power: power, Buildings: buildings,
            Mood: mood, Threat: threat, Wealth: wealth, Weather: weather, Research: research);
    }

    // ── Derivations ───────────────────────────────────────────────────────────

    private static SeasonContext DeriveSeason(DateStamp date)
    {
        if (date.Quadrum is null || date.Day is null)
            return new SeasonContext(null, null, null);

        var idx = Array.FindIndex(Quadrums,
            q => q.Equals(date.Quadrum, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            return new SeasonContext(date.Quadrum, null, null);

        var daysToNext   = DaysPerQuadrum - date.Day.Value + 1;
        var winterIdx    = Array.IndexOf(Quadrums, "Decembary");
        var quadrumsAway = (winterIdx - idx + 4) % 4;
        var daysToWinter = quadrumsAway == 0
            ? 0
            : (quadrumsAway - 1) * DaysPerQuadrum + daysToNext;

        return new SeasonContext(Quadrums[idx], daysToNext, daysToWinter);
    }

    private static ColonistsSummary DeriveColonistsSummary(IReadOnlyList<ColonistRecord> pawns)
    {
        var ordered = pawns
            .OrderBy(p => p.Mood)        // worst mood first — Mayor wants the squeaky wheels
            .ThenByDescending(p => p.Age)
            .ToList();

        var lines = ordered.Take(PawnLineCap).Select(p => new PawnLine(
            Name:       p.Name,
            Age:        p.Age,
            Mood:       p.Mood,
            Health:     p.Health,
            Hunger:     p.Hunger,
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
            var skills = pawns
                .SelectMany(p => p.Skills)
                .Where(s => string.Equals(s.Def, def, StringComparison.OrdinalIgnoreCase))
                .ToList();
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
        // M1 heuristic: very low health = downed; mild deficit = sick. Refine when
        // RimApiClient exposes a health-hediff endpoint.
        var downed = pawns.Count(p => p.Health < 0.30f);
        var sick   = pawns.Count(p => p.Health is >= 0.30f and < 0.85f);
        return new MedicalState(downed, sick, 0);
        // TODO: SurgeryPending requires a surgery-bill endpoint not yet exposed.
    }

    private static FoodSnapshot DeriveFood(FarmSnapshot farm, StockpileLedger stock, int colonistCount)
    {
        var foodUnits = stock.ItemsByDef
            .Where(kv => IsFoodDef(kv.Key))
            .Sum(kv => kv.Value);

        // Crude: 1.6 food units / colonist / day (approx ~0.6kg meal).
        // Null when we have no inventory data, so the Mayor doesn't reason from a fake zero.
        float? daysOfFood = stock.ItemsByDef.Count > 0 && colonistCount > 0
            ? foodUnits / (1.6f * colonistCount)
            : null;

        return new FoodSnapshot(
            TotalCrops:     farm.TotalCrops,
            AverageGrowth:  farm.AverageGrowth,
            ReadyToHarvest: farm.ReadyToHarvest,
            CropBreakdown:  farm.CropBreakdown
                                .Select(c => new CropBreakdown(c.Def, c.Count, c.AverageGrowth))
                                .ToList(),
            EstimatedFoodUnitsInStockpile: foodUnits,
            EstimatedDaysOfFood:           daysOfFood
        );
    }

    private static bool IsFoodDef(string def) =>
        def.Contains("Meal", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Pemmican", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Rice", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Potato", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Corn", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Berries", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Meat", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Milk", StringComparison.OrdinalIgnoreCase) ||
        def.Contains("Egg", StringComparison.OrdinalIgnoreCase);

    private static ResourceSnapshot DeriveResources(StockpileLedger stock)
    {
        // Empty until an inventory endpoint surfaces. See IngestionDispatcher.MapStockpiles.
        if (stock.ItemsByDef.Count == 0)
            return new ResourceSnapshot(
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new Dictionary<string, int>());

        static bool IsMaterial(string d) =>
            d.Equals("Steel", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("WoodLog", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("Plasteel", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("ComponentIndustrial", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("ComponentSpacer", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("Cloth", StringComparison.OrdinalIgnoreCase) ||
            d.Equals("Uranium", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Block", StringComparison.OrdinalIgnoreCase);

        static bool IsMedicine(string d) =>
            d.StartsWith("Medicine", StringComparison.OrdinalIgnoreCase);

        static bool IsWeapon(string d) =>
            d.StartsWith("Gun_", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Bow_", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("MeleeWeapon_", StringComparison.OrdinalIgnoreCase);

        var materials = stock.ItemsByDef.Where(kv => IsMaterial(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var medicine  = stock.ItemsByDef.Where(kv => IsMedicine(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var weapons   = stock.ItemsByDef.Where(kv => IsWeapon(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        return new ResourceSnapshot(materials, medicine, weapons);
    }

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
        Tally("Beds",        b => b.Def.Contains("Bed", StringComparison.OrdinalIgnoreCase) &&
                                  !b.Def.Contains("Hospital", StringComparison.OrdinalIgnoreCase));
        Tally("HospitalBeds",b => b.Def.Contains("Hospital", StringComparison.OrdinalIgnoreCase));
        Tally("Batteries",   b => b.Def.Contains("Battery", StringComparison.OrdinalIgnoreCase));
        Tally("Turrets",     b => b.Def.Contains("Turret", StringComparison.OrdinalIgnoreCase));
        Tally("Generators",  b => b.Def.Contains("Generator", StringComparison.OrdinalIgnoreCase) ||
                                  b.Def.Contains("SolarPanel", StringComparison.OrdinalIgnoreCase) ||
                                  b.Def.Contains("WindTurbine", StringComparison.OrdinalIgnoreCase));
        Tally("Coolers",     b => b.Def.Contains("Cooler", StringComparison.OrdinalIgnoreCase));
        Tally("Heaters",     b => b.Def.Contains("Heater", StringComparison.OrdinalIgnoreCase));

        return new BuildingsSummary(
            Total:       bs.Count,
            PoweredOff:  bs.Count(b => b.PowerOn == false),
            Damaged:     bs.Count(b => b.Hp < 0.5f),
            Strategic:   strategic
        );
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
        bool IsHostile(HostileLord l) =>
            l.JobType.Contains("Raid", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Siege", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Assault", StringComparison.OrdinalIgnoreCase) ||
            l.JobType.Contains("Sapper", StringComparison.OrdinalIgnoreCase);

        var hostiles = board.Lords.Where(IsHostile).ToList();

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
