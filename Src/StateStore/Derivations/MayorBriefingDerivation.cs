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
        // Dead colonists stay in the registry until the next ingestion drops them,
        // but the briefing should reflect the living colony only.
        var pawns    = s.Colonists.Value.Colonists.Where(p => !p.IsDead).ToList();

        var colonists = DeriveColonistsSummary(pawns);
        var skills    = DeriveSkillCoverage(pawns);
        var traits    = DeriveTraits(pawns);
        var medical   = DeriveMedical(pawns);
        var food      = DeriveFood(s.Farm.Value, s.Resources.Value, pawns.Count);
        var resources = DeriveResources(s.Resources.Value);
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
        var downed = pawns.Count(p => p.IsDowned);
        var sick   = pawns.Count(p => !p.IsDowned && p.Health < 0.85f);
        return new MedicalState(downed, sick, 0);
        // TODO: SurgeryPending requires a surgery-bill endpoint not yet exposed.
    }

    // Standard RimWorld nutrition: an adult colonist eats ~1.6 nutrition / day.
    // Used as the divisor for days-of-food when /resources/summary reports nutrition.
    private const float NutritionPerColonistPerDay = 1.6f;

    private static FoodSnapshot DeriveFood(FarmSnapshot farm, ResourceSummary resources, int colonistCount)
    {
        // EstimatedDaysOfFood is null when we have no nutrition signal yet
        // (mid-startup, or RIMAPI hasn't seen any meals). The Mayor's prompt
        // treats null as "food data unknown — request stockpile audit", not zero.
        float? daysOfFood = resources.TotalNutrition > 0f && colonistCount > 0
            ? resources.TotalNutrition / (NutritionPerColonistPerDay * colonistCount)
            : null;

        return new FoodSnapshot(
            TotalCrops:     farm.TotalCrops,
            AverageGrowth:  farm.AverageGrowth,
            ReadyToHarvest: farm.ReadyToHarvest,
            CropBreakdown:  farm.CropBreakdown
                                .Select(c => new CropBreakdown(c.Def, c.Count, c.AverageGrowth))
                                .ToList(),
            EstimatedFoodUnitsInStockpile: resources.FoodTotal,
            EstimatedDaysOfFood:           daysOfFood
        );
    }

    private static ResourceSnapshot DeriveResources(ResourceSummary r)
    {
        // /resources/summary gives us aggregated counts but not per-def detail.
        // Materials stays empty until /resources/stored returns non-empty data
        // (currently `{}` on a fresh map); medicine + weapons surface as a single
        // bucket each so the Mayor can at least see "do we have any?".
        Dictionary<string, int> materials = new();
        Dictionary<string, int> medicine  = r.MedicineTotal > 0
            ? new Dictionary<string, int> { ["Medicine"] = r.MedicineTotal }
            : new();
        Dictionary<string, int> weapons   = r.WeaponCount > 0
            ? new Dictionary<string, int> { ["Weapons"]  = r.WeaponCount }
            : new();

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
            l.JobType is not null && (
                l.JobType.Contains("Raid",    StringComparison.OrdinalIgnoreCase) ||
                l.JobType.Contains("Siege",   StringComparison.OrdinalIgnoreCase) ||
                l.JobType.Contains("Assault", StringComparison.OrdinalIgnoreCase) ||
                l.JobType.Contains("Sapper",  StringComparison.OrdinalIgnoreCase));

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
