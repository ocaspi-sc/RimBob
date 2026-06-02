namespace RimBob.Core.Briefings;

public sealed record WelfareSourceBriefing(
    long BriefingVersion,
    long GameTick,
    int ColonistCount,
    WelfareMoodSummary Mood,
    IReadOnlyList<WelfarePawnMood> WorstPawns,
    IReadOnlyList<WelfareNeedLow> NeedLows,
    WelfareRoomSummary Rooms,
    WelfareSleepSummary Sleep,
    WelfareRecreationSummary Recreation,
    WelfareThoughtDigest ThoughtDigest,
    WelfareDataCoverage DataCoverage
) : IBriefing;

public sealed record WelfareMoodSummary(
    float AverageMood,
    int BreakRiskCount,
    int StressedCount,
    int ContentCount
);

public sealed record WelfarePawnMood(
    string Id,
    string Name,
    float Mood,
    float Sleep,
    float Comfort,
    float Beauty,
    float Joy,
    float FreshAir,
    float DrugsDesire,
    IReadOnlyList<WelfareMoodThought> TopNegativeThoughts
);

public sealed record WelfareMoodThought(
    string DefName,
    string? Label,
    float MoodOffset,
    int StageIndex
);

public sealed record WelfareNeedLow(
    string PawnId,
    string PawnName,
    string Need,
    float Value
);

public sealed record WelfareRoomSummary(
    int Count,
    int BedroomCount,
    int PrisonCellCount,
    float? AverageImpressiveness,
    IReadOnlyList<WelfareRoomQuality> WorstRooms
);

public sealed record WelfareRoomQuality(
    string Id,
    string RoleLabel,
    float? Impressiveness,
    float? Beauty,
    float? Cleanliness,
    float? Space,
    float? Wealth,
    int CellsCount,
    bool IsPrisonCell,
    int OpenRoofCount
);

public sealed record WelfareSleepSummary(
    int BedCount,
    int ColonistCount,
    int BedDeficit,
    int UnroofedBedroomCount
);

public sealed record WelfareRecreationSummary(
    int JoyLowCount,
    int RecreationRoomCount,
    int JoySourceBuildingCount,
    bool HasRecreationSource
);

public sealed record WelfareThoughtDigest(
    IReadOnlyList<WelfareThoughtGroup> ByCategory
);

public sealed record WelfareThoughtGroup(
    ThoughtCategory Category,
    int PawnCount,
    float WorstOffset,
    string ExampleLabel
);

public sealed record WelfareDataCoverage(
    bool HasNeedLevels,
    bool HasMoodThoughts,
    bool HasRooms,
    bool HasRoomQuality,
    bool HasBuildings
);
