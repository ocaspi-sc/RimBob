using RimAI.Core.Briefings;
using RimAI.Core.Ministers;

namespace RimAI.Ministers.Mayor;

/// <summary>
/// Mayor's thin rules layer. Unlike feeder ministers (decide-or-escalate), the Mayor
/// almost always escalates to the LLM — the rules' job is to add prompt-shaping
/// hints ("lenses") for the next call. ~95% escalation rate per design.
/// </summary>
public sealed class MayorRules
{
    public MayorLensSet Evaluate(MayorBriefing briefing, ColonyContext _)
    {
        var prefills = new List<string>();

        var winterPrep = briefing.Season.DaysToWinter is { } d && d < 20;
        if (winterPrep)
            prefills.Add($"Winter prep lens: only {briefing.Season.DaysToWinter} days to winter — ensure a winter bullet sits in short_term.");

        var foodCrisis = briefing.Food.EstimatedDaysOfFood is { } days && days < 7;
        if (foodCrisis)
            prefills.Add($"Food crisis lens: only {briefing.Food.EstimatedDaysOfFood:F0} days of food remaining — force food bullet to position 1.");

        // M1: no flag channel; QuietDay is always true.
        const bool quietDay = true;

        var yearTwoTransition = briefing.Date.Year == 2 && briefing.Date.Quadrum == "Q1";
        if (yearTwoTransition)
            prefills.Add("Year-two transition lens: add an endgame-objective bullet to long_term (ship_launch | royal_favor | archonexus | maintenance).");

        if (quietDay && !winterPrep && !foodCrisis && !yearTwoTransition)
            prefills.Add("Quiet day: no Medium-or-higher flags fired in the last 24h. Keep update_notes brief and 'all clear' in tone.");

        return new MayorLensSet(winterPrep, foodCrisis, quietDay, yearTwoTransition, prefills);
    }
}

public sealed record MayorLensSet(
    bool WinterPrep,
    bool FoodCrisis,
    bool QuietDay,
    bool YearTwoTransition,
    IReadOnlyList<string> PromptPrefills
);
