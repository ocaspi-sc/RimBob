using System.Globalization;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Mayor;

public static class MayorAgendaBootstrap
{
    public static MayorAgendaInput Build(
        MayorBriefing briefing,
        IReadOnlyList<string> directives,
        IReadOnlyList<AgentFlag> activeFlags,
        string reason)
    {
        bool foodUnknown = briefing.Food.EstimatedDaysOfFood is null;
        bool foodCritical = briefing.Food.EstimatedDaysOfFood is { } days && days < 7f;
        bool activeThreat = briefing.Threat.ActiveRaid || briefing.Threat.HostileLordCount > 0;
        bool noLiveStateYet = briefing.GameTick == 0 && briefing.Colonists.Count == 0;

        Dictionary<string, string> state = new(StringComparer.OrdinalIgnoreCase)
        {
            ["food"] = FoodSentence(briefing.Food),
            ["defense"] = activeThreat
                ? $"Active hostile pressure detected: {briefing.Threat.HostileLordCount} hostile lord(s), {FormatFloat(briefing.Threat.TotalThreatPoints)} threat points."
                : "No active raid is visible in the current briefing.",
            ["welfare"] = briefing.Colonists.Count == 0
                ? "No colonist readout is available yet."
                : $"Colony has {briefing.Colonists.Count} colonist(s), {briefing.Mood.BreakRiskCount} break-risk pawn(s), and {briefing.Medical.Downed} downed pawn(s).",
            ["construction"] = $"Visible buildings: {briefing.Buildings.Total}; powered off: {briefing.Buildings.PoweredOff}; damaged: {briefing.Buildings.Damaged}.",
            ["research"] = string.IsNullOrWhiteSpace(briefing.Research.CurrentProject)
                ? "No active research project is visible in the current briefing."
                : $"Current research: {briefing.Research.CurrentProject} at {FormatPercent(briefing.Research.Progress)} progress.",
        };

        if (activeFlags.Count > 0)
            state["cabinet"] = $"{activeFlags.Count} medium-or-higher flag(s) are active; highest priority: {activeFlags[0].Summary}";

        if (directives.Count > 0)
            state["mayor"] = "Deterministic Mayor directives are active while waiting for the next full agenda run.";

        List<AgendaPriority> shortTerm = new(5);
        if (foodUnknown)
        {
            shortTerm.Add(new AgendaPriority(
                "bootstrap_food_visibility",
                "Confirm food stockpiles, harvestable plants, and meal production after the next live state refresh.",
                AgendaPriorityStatus.Active));
        }
        else if (foodCritical)
        {
            shortTerm.Add(new AgendaPriority(
                "bootstrap_food_emergency",
                $"Stabilize food immediately; only {FormatFloat(briefing.Food.EstimatedDaysOfFood!.Value)} days of food are visible.",
                AgendaPriorityStatus.Active));
        }

        if (activeThreat)
        {
            shortTerm.Add(new AgendaPriority(
                "bootstrap_active_threat",
                "Resolve the active threat before taking growth or comfort work.",
                AgendaPriorityStatus.Active));
        }

        if (briefing.Buildings.Total == 0 || briefing.Buildings.PoweredOff > 0 || noLiveStateYet)
        {
            shortTerm.Add(new AgendaPriority(
                "bootstrap_infrastructure_readout",
                "Verify core infrastructure readouts: shelter, beds, power, cooking, and food storage.",
                AgendaPriorityStatus.Active));
        }

        shortTerm.Add(new AgendaPriority(
            "bootstrap_replace_with_mayor_run",
            "Run the Mayor again when live state and LLM access are healthy so this bootstrap plan can be replaced.",
            AgendaPriorityStatus.Active));

        IReadOnlyDictionary<string, string> cabinetDirection = BuildCabinetDirection(foodUnknown, foodCritical, activeThreat, noLiveStateYet);
        string economicPosture = foodUnknown || foodCritical || noLiveStateYet ? "survival" : "consolidation";
        string summary = noLiveStateYet
            ? "Bootstrap agenda from cold state; waiting for live colony ingestion."
            : "Bootstrap agenda from current briefing; replace with the next successful Mayor LLM run.";

        return new MayorAgendaInput(
            Posture: new MayorPosture(economicPosture, "defensive", summary),
            StateOfTheUnion: state,
            UpdateNotes: $"{reason} Host initialized a conservative local agenda because no persisted Mayor agenda existed.",
            ShortTerm: shortTerm.Take(5).ToArray(),
            LongTerm:
            [
                new AgendaPriority(
                    "bootstrap_food_buffer",
                    "Build toward a visible food buffer of at least 7 days before expansion work.",
                    AgendaPriorityStatus.Active),
                new AgendaPriority(
                    "bootstrap_operational_stability",
                    "Keep basic shelter, power, cooking, and storage stable while the cabinet gathers better evidence.",
                    AgendaPriorityStatus.Active),
            ],
            CabinetDirection: cabinetDirection,
            GuideCitations: []);
    }

    private static IReadOnlyDictionary<string, string> BuildCabinetDirection(
        bool foodUnknown,
        bool foodCritical,
        bool activeThreat,
        bool noLiveStateYet)
    {
        Dictionary<string, string> direction = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Mayor"] = "Replace this bootstrap agenda with a full LLM agenda as soon as provider quota and live state allow."
        };

        if (foodUnknown)
            direction["Food"] = "Prioritize food visibility: stockpiles, harvestables, meal production, and cooking bottlenecks.";
        else if (foodCritical)
            direction["Food"] = "Treat food security as the top cabinet concern until the buffer is above 7 days.";

        if (activeThreat)
            direction["Defense"] = "Handle active hostile pressure before non-survival work.";

        if (noLiveStateYet)
            direction["System"] = "Live state has not populated yet; use this only as a startup placeholder.";

        return direction;
    }

    private static string FoodSentence(FoodSnapshot food)
    {
        if (food.EstimatedDaysOfFood is null)
            return "Food visibility is incomplete; stockpiles, harvestables, and meal production need confirmation.";

        string days = FormatFloat(food.EstimatedDaysOfFood.Value);
        if (food.EstimatedDaysOfFood.Value < 2f)
            return $"Food is critical at {days} visible day(s); emergency food work comes first.";

        if (food.EstimatedDaysOfFood.Value < 7f)
            return $"Food buffer is thin at {days} visible day(s); stabilize before growth.";

        return $"Food buffer is {days} visible day(s).";
    }

    private static string FormatFloat(float value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatPercent(float? value)
    {
        if (value is null)
            return "unknown";

        return (value.Value * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }
}
