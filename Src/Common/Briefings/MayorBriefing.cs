namespace RimBob.Core.Briefings;

/// <summary>
/// Colony-wide briefing the Mayor reads once per in-game day. Target ≤ ~800 tokens
/// when serialized. M1: only feeder; CoS digest, trend windows, and player-feedback
/// aggregation come online M2/M3+. See Docs/design/ministers/mayor.md.
/// </summary>
public sealed record MayorBriefing(
    long             BriefingVersion, // monotonic counter from BriefingCache; used in decision log
    GameDate         Date,
    long             GameTick,
    SeasonContext    Season,
    ColonistsSummary Colonists,
    SkillCoverage    Skills,
    TraitCallouts    Traits,
    MedicalState     Medical,
    int              Prisoners,
    FoodSnapshot     Food,
    ResourceSnapshot Resources,
    PowerSnapshot    Power,
    BuildingsSummary Buildings,
    MoodSnapshot     Mood,
    ThreatSnapshot   Threat,
    WealthSnapshot   Wealth,
    WeatherSnapshot  Weather,
    ResearchSnapshot Research
    // TODO (M2+): TrendWindows.
    // TODO (M3+): CoSDigest, RecentPlayerFeedback, AgendaPosture.
    // TODO: ScheduleSnapshot — no RIMAPI endpoint yet.
) : IBriefing;

public sealed record GameDate(
    string RawRimWorldDate,
    int? RimWorldYear,
    string? Quadrum,
    int? QuadrumDay,
    int? Hour,
    long GameTick,
    double TotalDays,
    long CompletedDays,
    long ColonyDay,
    long ColonyYear,
    int DayOfYear,
    string Label);

public static class GameTime
{
    public const long TicksPerGameDay = 60_000;
    public const int DaysPerYear = 60;

    public static GameDate Create(
        string? rawRimWorldDate,
        long gameTick,
        int? rimWorldYear,
        string? quadrum,
        int? quadrumDay,
        int? hour)
    {
        if (gameTick < 0)
            throw new ArgumentOutOfRangeException(nameof(gameTick), gameTick, "Game tick must be non-negative.");

        long completedDays = gameTick / TicksPerGameDay;
        long colonyDay = completedDays + 1;
        long colonyYear = completedDays / DaysPerYear + 1;
        int dayOfYear = (int)(completedDays % DaysPerYear) + 1;

        return new GameDate(
            RawRimWorldDate: rawRimWorldDate ?? "",
            RimWorldYear: rimWorldYear,
            Quadrum: quadrum,
            QuadrumDay: quadrumDay,
            Hour: hour,
            GameTick: gameTick,
            TotalDays: gameTick / (double)TicksPerGameDay,
            CompletedDays: completedDays,
            ColonyDay: colonyDay,
            ColonyYear: colonyYear,
            DayOfYear: dayOfYear,
            Label: FormatLabel(colonyYear, colonyDay, quadrum, quadrumDay, hour));
    }

    public static string FormatLabel(GameDate date) => date.Label;

    private static string FormatLabel(long colonyYear, long colonyDay, string? quadrum, int? quadrumDay, int? hour)
    {
        string label = $"Y{colonyYear} D{colonyDay}";
        if (!string.IsNullOrWhiteSpace(quadrum) && quadrumDay is not null)
            label += $", {quadrum} {quadrumDay}";
        if (hour is not null)
            label += $", {hour}h";
        return label;
    }
}

public sealed record SeasonContext(
    string? CurrentSeason,
    int?    DaysToNextSeason,
    int?    DaysToWinter
    // TODO: cold-snap window — needs weather forecast endpoint we don't yet expose.
);

public sealed record ColonistsSummary(
    int Count,
    int Adults,
    int Children,
    IReadOnlyList<PawnLine> Pawns,
    int? AdditionalNotShown
);

public sealed record PawnLine(
    string Id,
    string Name,
    int    Age,
    float  Mood,
    float  Health,
    float  Hunger,
    bool   IsDowned,   // authoritative incapacitation flag from RIMAPI; do not infer from Health
    string? CurrentJob,
    string? TopSkill   // "Plants 14*" — level + passion glyph
);

public sealed record SkillCoverage(IReadOnlyDictionary<string, SkillCoverageEntry> ByDef);

public sealed record SkillCoverageEntry(int BestLevel, int PassionatePawns, int QualifiedPawns);

public sealed record TraitCallouts(
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Strengths
);

public sealed record MedicalState(int Downed, int Sick, int SurgeryPending);

public sealed record FoodSnapshot(
    int    TotalCrops,
    float  AverageGrowth,
    int    ReadyToHarvest,
    IReadOnlyList<CropBreakdown> CropBreakdown,
    int    EstimatedFoodUnitsInStockpile,
    float? EstimatedDaysOfFood
);

public sealed record CropBreakdown(string Def, int Count, float AverageGrowth);

public sealed record ResourceSnapshot(
    IReadOnlyDictionary<string, int> Materials,
    IReadOnlyDictionary<string, int> Medicine,
    IReadOnlyDictionary<string, int> Weapons
);

public sealed record PowerSnapshot(
    float ProductionW,
    float ConsumptionW,
    float StoredWd,
    float CapacityWd,
    float NetW
);

public sealed record BuildingsSummary(
    int Total,
    int PoweredOff,
    int Damaged,
    IReadOnlyDictionary<string, int> Strategic
);

public sealed record MoodSnapshot(
    float AverageMood,
    int   BreakRiskCount,
    int   StressedCount,
    int   ContentCount
);

public sealed record ThreatSnapshot(
    bool  ActiveRaid,
    int   HostileLordCount,
    float TotalThreatPoints,
    IReadOnlyList<RaidDigest> ActiveRaids,
    IReadOnlyList<string>     RecentIncidents
);

public sealed record RaidDigest(string? JobType, string? FactionId, float? ThreatPoints, int PawnCount);

public sealed record WealthSnapshot(float Colony, int ColonistCount, float WealthPerColonist);

public sealed record WeatherSnapshot(string Def, float TemperatureC, float RainRate);

public sealed record ResearchSnapshot(string? CurrentProject, float? Progress);
