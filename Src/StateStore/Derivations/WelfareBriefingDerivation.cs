using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State.Derivations.Common;

namespace RimBob.State.Derivations;

public static class WelfareBriefingDerivation
{
    private const float BreakRiskMood = 0.35f;
    private const float StressedTopMood = 0.50f;
    private const float ContentMood = 0.65f;
    private const float LowNeedThreshold = 0.35f;

    public static WelfareSourceBriefing Compute(ColonyState state, long briefingVersion = 0)
    {
        IReadOnlyList<ColonistRecord> pawns = PawnDeriver.LivingColonists(state.Colonists.Value.Colonists);
        IReadOnlyList<RoomRecord> rooms = state.Rooms.Value.Rooms;

        return new WelfareSourceBriefing(
            BriefingVersion: briefingVersion,
            GameTick: state.Economy.Value.Tick,
            ColonistCount: pawns.Count,
            Mood: DeriveMood(pawns),
            WorstPawns: DeriveWorstPawns(pawns),
            NeedLows: DeriveNeedLows(pawns),
            Rooms: DeriveRooms(rooms),
            DataCoverage: DeriveCoverage(state, pawns, rooms));
    }

    private static WelfareMoodSummary DeriveMood(IReadOnlyList<ColonistRecord> pawns)
    {
        if (pawns.Count == 0)
            return new WelfareMoodSummary(0f, 0, 0, 0);

        return new WelfareMoodSummary(
            AverageMood: pawns.Average(pawn => pawn.Mood),
            BreakRiskCount: pawns.Count(pawn => pawn.Mood < BreakRiskMood),
            StressedCount: pawns.Count(pawn => pawn.Mood is >= BreakRiskMood and < StressedTopMood),
            ContentCount: pawns.Count(pawn => pawn.Mood > ContentMood));
    }

    private static IReadOnlyList<WelfarePawnMood> DeriveWorstPawns(IReadOnlyList<ColonistRecord> pawns) =>
        pawns
            .OrderBy(pawn => pawn.Mood)
            .ThenBy(pawn => pawn.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(pawn => new WelfarePawnMood(
                Id: pawn.Id,
                Name: pawn.Name,
                Mood: pawn.Mood,
                Sleep: pawn.Sleep,
                Comfort: pawn.Comfort,
                Beauty: pawn.Beauty,
                Joy: pawn.Joy,
                FreshAir: pawn.FreshAir,
                DrugsDesire: pawn.DrugsDesire,
                TopNegativeThoughts: NegativeThoughts(pawn)))
            .ToList();

    private static IReadOnlyList<WelfareMoodThought> NegativeThoughts(ColonistRecord pawn) =>
        (pawn.MoodThoughts ?? [])
            .Where(thought => thought.MoodOffset < 0f)
            .OrderBy(thought => thought.MoodOffset)
            .ThenBy(thought => thought.DefName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(thought => new WelfareMoodThought(
                DefName: thought.DefName,
                Label: thought.Label,
                MoodOffset: thought.MoodOffset,
                StageIndex: thought.StageIndex))
            .ToList();

    private static IReadOnlyList<WelfareNeedLow> DeriveNeedLows(IReadOnlyList<ColonistRecord> pawns) =>
        pawns
            .SelectMany(PawnNeedLows)
            .OrderBy(need => need.Value)
            .ThenBy(need => need.PawnName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(need => need.Need, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

    private static IReadOnlyList<WelfareNeedLow> PawnNeedLows(ColonistRecord pawn)
    {
        List<WelfareNeedLow> lows = [];
        AddNeedLow(lows, pawn, "sleep", pawn.Sleep);
        AddNeedLow(lows, pawn, "comfort", pawn.Comfort);
        AddNeedLow(lows, pawn, "beauty", pawn.Beauty);
        AddNeedLow(lows, pawn, "joy", pawn.Joy);
        AddNeedLow(lows, pawn, "fresh_air", pawn.FreshAir);
        if (pawn.DrugsDesire > 0f)
            AddNeedLow(lows, pawn, "drugs_desire", pawn.DrugsDesire);
        return lows;
    }

    private static void AddNeedLow(
        List<WelfareNeedLow> lows,
        ColonistRecord pawn,
        string need,
        float value)
    {
        if (value >= LowNeedThreshold)
            return;

        lows.Add(new WelfareNeedLow(pawn.Id, pawn.Name, need, value));
    }

    private static WelfareRoomSummary DeriveRooms(IReadOnlyList<RoomRecord> rooms)
    {
        IReadOnlyList<RoomRecord> roomsWithImpressiveness = rooms
            .Where(room => room.Impressiveness is not null)
            .ToList();

        float? averageImpressiveness = roomsWithImpressiveness.Count == 0
            ? null
            : roomsWithImpressiveness.Average(room => room.Impressiveness!.Value);

        return new WelfareRoomSummary(
            Count: rooms.Count,
            BedroomCount: rooms.Count(IsBedroom),
            PrisonCellCount: rooms.Count(room => room.IsPrisonCell),
            AverageImpressiveness: averageImpressiveness,
            WorstRooms: rooms
                .Where(HasAnyQualityStat)
                .OrderBy(room => room.Impressiveness ?? float.MaxValue)
                .ThenBy(room => room.Beauty ?? float.MaxValue)
                .ThenBy(room => room.Cleanliness ?? float.MaxValue)
                .ThenBy(room => room.Id, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(room => new WelfareRoomQuality(
                    Id: room.Id,
                    RoleLabel: room.RoleLabel,
                    Impressiveness: room.Impressiveness,
                    Beauty: room.Beauty,
                    Cleanliness: room.Cleanliness,
                    Space: room.Space,
                    Wealth: room.Wealth,
                    CellsCount: room.CellsCount,
                    IsPrisonCell: room.IsPrisonCell,
                    OpenRoofCount: room.OpenRoofCount))
                .ToList());
    }

    private static bool IsBedroom(RoomRecord room) =>
        room.RoleLabel.Contains("bedroom", StringComparison.OrdinalIgnoreCase) ||
        room.ContainedBedIds.Count > 0;

    private static bool HasAnyQualityStat(RoomRecord room) =>
        room.Impressiveness is not null ||
        room.Beauty is not null ||
        room.Cleanliness is not null ||
        room.Space is not null ||
        room.Wealth is not null;

    private static WelfareDataCoverage DeriveCoverage(
        ColonyState state,
        IReadOnlyList<ColonistRecord> pawns,
        IReadOnlyList<RoomRecord> rooms) =>
        new(
            HasNeedLevels: pawns.Any(pawn =>
                pawn.Sleep > 0f ||
                pawn.Comfort > 0f ||
                pawn.Beauty > 0f ||
                pawn.Joy > 0f ||
                pawn.FreshAir > 0f ||
                pawn.DrugsDesire > 0f),
            HasMoodThoughts: pawns.Any(pawn => (pawn.MoodThoughts ?? []).Count > 0),
            HasRooms: state.Rooms.Version > 0,
            HasRoomQuality: rooms.Any(HasAnyQualityStat));
}
