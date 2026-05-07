using Microsoft.Extensions.Logging;
using RimAI.Coordination;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.LLM;
using RimAI.State;

namespace RimAI.Ministers.Mayor;

/// <summary>
/// The Mayor synthesises the colony-wide briefing into a living MayorAgenda once per
/// in-game day. M1 centerpiece. See Docs/design/ministers/mayor.md.
/// </summary>
public sealed class Mayor(
    BriefingCache       briefings,
    MayorRules          rules,
    AgendaStore         agendaStore,
    LlmClient           llm,
    AdviceBus           bus,
    ILogger<Mayor>      log
) : IMinister
{
    private const int ShortTermCap = 5;

    public string Name => "Mayor";

    public async Task RunPlayCycle(CancellationToken ct)
    {
        MayorBriefing briefing             = briefings.GetMayorBriefing();
        MayorLensSet lens                  = rules.Evaluate(briefing, ColonyContext.Default);
        Core.Advice.MayorAgenda? previous  = agendaStore.Current;

        log.LogInformation(
            "Mayor wake briefing_version={BriefingVersion} previous_agenda_version={PrevVersion} lenses=[{Lenses}]",
            briefing.BriefingVersion, previous?.Version ?? 0, FormatLenses(lens));

        Core.Advice.MayorAgendaInput? input = await CallLlmWithRetryAsync(briefing, previous, lens.PromptPrefills, ct);
        if (input is null)
        {
            log.LogError("Mayor LLM call failed twice; agenda not updated this turn.");
            return;
        }

        Core.Advice.MayorAgenda stamped = agendaStore.Update(input, FormatTick(briefing));
        bus.Publish(new AgendaUpdated(stamped));

        log.LogInformation(
            "Mayor agenda v{Version} stored and published (tick={Tick} short_term={ShortCount})",
            stamped.Version, stamped.UpdatedInGameTick, stamped.ShortTerm.Count);
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;  // M6

    private async Task<Core.Advice.MayorAgendaInput?> CallLlmWithRetryAsync(
        MayorBriefing briefing, Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> prefills, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Core.Advice.MayorAgendaInput input = await llm.CallMayorAsync(briefing, previous, prefills, ct);
                if (input.ShortTerm.Count > ShortTermCap)
                {
                    log.LogWarning(
                        "Mayor agenda has {Count} short_term items (cap is {Cap}); attempt {Attempt}",
                        input.ShortTerm.Count, ShortTermCap, attempt);
                    if (attempt == 2)
                    {
                        // Truncate rather than drop the whole agenda after the second try.
                        return input with { ShortTerm = input.ShortTerm.Take(ShortTermCap).ToList() };
                    }
                    continue;
                }
                return input;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (attempt == 1)
                    log.LogWarning(ex, "Mayor LLM call failed (attempt 1); retrying once");
                else
                    log.LogError(ex, "Mayor LLM call failed (attempt 2); giving up this turn");
            }
        }
        return null;
    }

    private static string FormatTick(MayorBriefing b) =>
        $"Y{b.Date.Year ?? 0}{b.Date.Quadrum ?? "?"}D{b.Date.Day ?? 0}";

    private static string FormatLenses(MayorLensSet l)
    {
        List<string> fired = new(4);
        if (l.WinterPrep)        fired.Add("winter_prep");
        if (l.FoodCrisis)        fired.Add("food_crisis");
        if (l.YearTwoTransition) fired.Add("year_two_transition");
        if (l.QuietDay && fired.Count == 0) fired.Add("quiet_day");
        return string.Join(',', fired);
    }
}
