using RimBob.Core.Briefings;

namespace RimBob.Knowledge;

public sealed class MayorRagQueryBuilder : IRagQueryBuilder<MayorBriefing>
{
    public string BuildQuery(MayorBriefing briefing, IReadOnlyList<string> directives) =>
        Build(briefing, directives);

    public static string Build(MayorBriefing briefing, IReadOnlyList<string> agendaDirectives)
    {
        List<string> parts = new(8);

        if (briefing.Date.Quadrum is not null && briefing.Date.Day is not null)
            parts.Add($"Y{briefing.Date.Year ?? 0} {briefing.Date.Quadrum} day {briefing.Date.Day}.");
        if (briefing.Season.CurrentSeason is not null)
            parts.Add($"Season: {briefing.Season.CurrentSeason}.");
        if (briefing.Season.DaysToWinter is { } daysToWinter)
            parts.Add($"Days to winter: {daysToWinter}.");

        parts.Add($"Colonists: {briefing.Colonists.Count}; mood {briefing.Mood.AverageMood:F2}; downed {briefing.Medical.Downed}.");
        if (briefing.Food.EstimatedDaysOfFood is { } days)
            parts.Add($"Food: {days:F1} days; stockpile {briefing.Food.EstimatedFoodUnitsInStockpile}.");
        if (briefing.Threat.ActiveRaid)
            parts.Add($"Active raid: {briefing.Threat.HostileLordCount} hostile groups, {briefing.Threat.TotalThreatPoints:F0} threat points.");
        parts.Add($"Wealth: {briefing.Wealth.Colony:F0}.");
        parts.Add($"Weather: {briefing.Weather.Def}, {briefing.Weather.TemperatureC:F1}\u00b0C.");
        if (briefing.Research.CurrentProject is not null)
            parts.Add($"Research: {briefing.Research.CurrentProject}.");

        foreach (string directive in agendaDirectives)
            parts.Add(directive);

        parts.Add("Which RimWorld guide passages give the most relevant strategic advice?");
        return string.Join(' ', parts);
    }
}
