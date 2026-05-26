using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Mayor;

/// <summary>
/// Mayor's thin rules layer. Unlike feeder ministers (decide-or-escalate), the Mayor
/// almost always escalates to the LLM; the rules' job is to add agenda directives
/// that constrain the next call. ~95% escalation rate per design.
/// </summary>
public sealed class MayorAgendaRules
{
    public MayorDirectiveSet Evaluate(
        MayorBriefing briefing,
        ColonyContext _,
        IReadOnlyList<AgentFlag>? activeFlags = null)
    {
        List<string> directives = new();

        bool winterPrepRequired = briefing.Season.DaysToWinter is { } d && d < 20;
        if (winterPrepRequired)
            directives.Add($"Winter prep directive: only {briefing.Season.DaysToWinter} days to winter — ensure a winter bullet sits in short_term.");

        bool foodSecurityCritical = briefing.Food.EstimatedDaysOfFood is { } days && days < 7;
        if (foodSecurityCritical)
            directives.Add($"Food security directive: only {briefing.Food.EstimatedDaysOfFood:F0} days of food remaining — force food bullet to position 1.");

        IReadOnlyList<AgentFlag> mediumOrHigher = activeFlags ?? [];
        foreach (AgentFlag flag in mediumOrHigher)
        {
            directives.Add($"Feeder flag from {flag.SourceMinister} ({flag.Severity}, {flag.Domain}): {flag.Summary}. Reflect this in the matching state_of_the_union category and agenda rationale when relevant.");
        }

        bool quietDay = mediumOrHigher.Count == 0;

        bool yearTwoTransition = briefing.Date.ColonyYear == 2 && briefing.Date.DayOfYear <= 15;
        if (yearTwoTransition)
            directives.Add("Year-two transition directive: add an endgame-objective bullet to long_term (ship_launch | royal_favor | archonexus | maintenance).");

        if (quietDay && !winterPrepRequired && !foodSecurityCritical && !yearTwoTransition)
            directives.Add("Quiet day directive: no Medium-or-higher flags fired in the last 24h. Keep update_notes brief and 'all clear' in tone.");

        return new MayorDirectiveSet(winterPrepRequired, foodSecurityCritical, quietDay, yearTwoTransition, directives);
    }
}

public sealed record MayorDirectiveSet(
    bool WinterPrepRequired,
    bool FoodSecurityCritical,
    bool QuietDay,
    bool YearTwoTransition,
    IReadOnlyList<string> Directives
);
