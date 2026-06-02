using System.Globalization;
using RimBob.Core.Briefings;

namespace RimBob.Ministers.Welfare;

public static class WelfareStateSummary
{
    public static string Build(WelfareSourceBriefing briefing)
    {
        string mood = $"Mood: avg {briefing.Mood.AverageMood.ToString("P0", CultureInfo.InvariantCulture)}, {briefing.Mood.ContentCount} content, {briefing.Mood.StressedCount} stressed, {briefing.Mood.BreakRiskCount} break-risk.";
        string shelter = briefing.Rooms.BedroomCount == 0
            ? "Shelter: no bedroom/barracks beds visible."
            : $"Shelter: {briefing.Rooms.BedroomCount} bed-bearing room{Plural(briefing.Rooms.BedroomCount)} visible.";
        string driver = TopDriver(briefing);
        string coverage = $"Coverage: needs {YesNo(briefing.DataCoverage.HasNeedLevels)}, thoughts {YesNo(briefing.DataCoverage.HasMoodThoughts)}, rooms {YesNo(briefing.DataCoverage.HasRooms)}, room quality {YesNo(briefing.DataCoverage.HasRoomQuality)}.";

        return string.Join(Environment.NewLine, [mood, shelter, driver, coverage]);
    }

    private static string TopDriver(WelfareSourceBriefing briefing)
    {
        WelfarePawnMood? pawn = briefing.WorstPawns.FirstOrDefault();
        if (pawn is null)
            return "Top driver: no pawn mood detail.";

        WelfareMoodThought? thought = pawn.TopNegativeThoughts.FirstOrDefault();
        if (thought is null)
            return $"Top driver: {pawn.Name} is lowest mood; no negative thought detail.";

        string label = string.IsNullOrWhiteSpace(thought.Label) ? thought.DefName : thought.Label.Trim();
        return $"Top driver: {pawn.Name} / {label} ({thought.MoodOffset.ToString("0.#", CultureInfo.InvariantCulture)} mood).";
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Plural(int count) => count == 1 ? "" : "s";
}
