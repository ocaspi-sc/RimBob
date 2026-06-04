using RimBob.Core.Briefings;

namespace RimBob.Knowledge;

public sealed class WelfareRagQueryBuilder : IRagQueryBuilder<WelfareSourceBriefing>
{
    public string BuildQuery(WelfareSourceBriefing briefing, IReadOnlyList<string> directives) =>
        Build(briefing);

    public static string Build(WelfareSourceBriefing briefing)
    {
        List<string> parts =
        [
            "RimWorld mood needs recreation rooms beauty comfort sleep social fights ideology temperature apparel guest animal welfare.",
            $"Average mood {briefing.Mood.AverageMood:F2}; stressed {briefing.Mood.StressedCount}; break risk {briefing.Mood.BreakRiskCount}.",
            $"Beds {briefing.Sleep.BedCount}/{briefing.ColonistCount}; bed deficit {briefing.Sleep.BedDeficit}; unroofed sleeping rooms {briefing.Sleep.UnroofedBedroomCount}.",
            $"Low joy {briefing.Recreation.JoyLowCount}; recreation buildings {briefing.Recreation.JoySourceBuildingCount}; recreation rooms {briefing.Recreation.RecreationRoomCount}."
        ];

        if (briefing.ThoughtDigest.ByCategory.Count > 0)
        {
            parts.Add("Thought drivers: " + string.Join("; ",
                briefing.ThoughtDigest.ByCategory
                    .Take(5)
                    .Select(group => $"{group.Category} {group.WorstOffset:F1} {group.ExampleLabel}")));
        }

        if (briefing.NeedLows.Count > 0)
        {
            parts.Add("Low needs: " + string.Join("; ",
                briefing.NeedLows
                    .Take(5)
                    .Select(need => $"{need.PawnName} {need.Need} {need.Value:F2}")));
        }

        if (briefing.Rooms.WorstRooms.Count > 0)
        {
            parts.Add("Worst rooms: " + string.Join("; ",
                briefing.Rooms.WorstRooms
                    .Take(3)
                    .Select(room => $"{room.RoleLabel} impressiveness {ValueOrUnknown(room.Impressiveness)} beauty {ValueOrUnknown(room.Beauty)} cleanliness {ValueOrUnknown(room.Cleanliness)} open roof {room.OpenRoofCount}")));
        }

        parts.Add($"Coverage needs={briefing.DataCoverage.HasNeedLevels}; thoughts={briefing.DataCoverage.HasMoodThoughts}; rooms={briefing.DataCoverage.HasRooms}; room quality={briefing.DataCoverage.HasRoomQuality}; buildings={briefing.DataCoverage.HasBuildings}.");
        return string.Join(' ', parts);
    }

    private static string ValueOrUnknown(float? value) =>
        value is null ? "unknown" : value.Value.ToString("F1");
}
