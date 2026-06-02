using System.Globalization;
using RimBob.Core.Briefings;

namespace RimBob.Ministers.Welfare;

public static class WelfareStateSummary
{
    public static string Build(WelfareSourceBriefing briefing)
    {
        string mood = $"Mood: avg {briefing.Mood.AverageMood.ToString("P0", CultureInfo.InvariantCulture)}, {briefing.Mood.ContentCount} content, {briefing.Mood.StressedCount} stressed, {briefing.Mood.BreakRiskCount} break-risk.";
        string shelter = $"Shelter: {briefing.Sleep.BedCount}/{briefing.ColonistCount} beds, {briefing.Sleep.BedDeficit} deficit, {briefing.Sleep.UnroofedBedroomCount} unroofed sleeping room{Plural(briefing.Sleep.UnroofedBedroomCount)}.";
        string recreation = $"Recreation: {briefing.Recreation.JoyLowCount} low-joy pawn{Plural(briefing.Recreation.JoyLowCount)}, {briefing.Recreation.JoySourceBuildingCount} joy buildings, {briefing.Recreation.RecreationRoomCount} rec rooms.";
        string thoughts = ThoughtSummary(briefing);
        string driver = TopDriver(briefing);
        string coverage = $"Coverage: needs {YesNo(briefing.DataCoverage.HasNeedLevels)}, thoughts {YesNo(briefing.DataCoverage.HasMoodThoughts)}, rooms {YesNo(briefing.DataCoverage.HasRooms)}, room quality {YesNo(briefing.DataCoverage.HasRoomQuality)}, buildings {YesNo(briefing.DataCoverage.HasBuildings)}.";

        return string.Join(Environment.NewLine, [mood, shelter, recreation, thoughts, driver, coverage]);
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

    private static string ThoughtSummary(WelfareSourceBriefing briefing)
    {
        WelfareThoughtGroup? group = briefing.ThoughtDigest.ByCategory.FirstOrDefault();
        if (group is null)
            return "Thoughts: no negative thought categories.";

        return $"Thoughts: {group.Category} leads ({group.PawnCount} pawn{Plural(group.PawnCount)}, worst {group.WorstOffset.ToString("0.#", CultureInfo.InvariantCulture)}: {group.ExampleLabel}).";
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Plural(int count) => count == 1 ? "" : "s";
}
