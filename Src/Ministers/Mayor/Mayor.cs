using Microsoft.Extensions.Logging;
using RimAI.Core.Ministers;
using RimAI.State;

namespace RimAI.Ministers.Mayor;

/// <summary>
/// The Mayor synthesises the colony-wide briefing into a living MayorAgenda once per
/// in-game day. M1 centerpiece. See Docs/design/ministers/mayor.md.
/// </summary>
public sealed class Mayor(
    BriefingCache       briefings,
    MayorRules          rules,
    ILogger<Mayor>      log
) : IMinister
{
    public string Name => "Mayor";

    public Task RunPlayCycle(CancellationToken ct)
    {
        var briefing = briefings.GetMayorBriefing();
        var lens     = rules.Evaluate(briefing, ColonyContext.Default);

        log.LogInformation(
            "Mayor wake briefing_version={BriefingVersion} lenses=[{Lenses}] prefills={PrefillCount}",
            briefing.BriefingVersion,
            FormatLenses(lens),
            lens.PromptPrefills.Count);

        // TODO (W3+W4): read previous MayorAgenda from AgendaStore, build prompt via PromptBuilder,
        // call LlmClient.CallMayorAsync, validate ShortTerm <= 5 (retry once), AgendaStore.Update,
        // AdviceBus.Publish(new AgendaUpdated(...)).
        return Task.CompletedTask;
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;  // M6

    private static string FormatLenses(MayorLensSet l)
    {
        var fired = new List<string>(4);
        if (l.WinterPrep)        fired.Add("winter_prep");
        if (l.FoodCrisis)        fired.Add("food_crisis");
        if (l.YearTwoTransition) fired.Add("year_two_transition");
        if (l.QuietDay && fired.Count == 0) fired.Add("quiet_day");
        return string.Join(',', fired);
    }
}
