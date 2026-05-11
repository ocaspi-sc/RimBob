using System.Text.Json;
using Microsoft.Extensions.Logging;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.State;

namespace RimAI.Ministers.Mayor;

/// <summary>
/// The Mayor synthesises the colony-wide briefing into a living MayorAgenda once per
/// in-game day. M1 centerpiece. See Docs/design/ministers/mayor.md.
/// </summary>
public sealed class Mayor(
    BriefingCache       briefings,
    MayorAgendaRules          rules,
    AgendaStore         agendaStore,
    LlmClient           llm,
    PromptBuilder       prompts,
    AdviceBus           bus,
    MayorStatus         status,
    MayorRagRetriever      retriever,
    ILogger<Mayor>      log
) : IMinister
{
    private const int ShortTermCap = 5;
    private static readonly string PromptDumpPath     = Path.Combine("logs", "mayor-prompt-latest.md");
    private static readonly string ManualResponsePath = Path.Combine("logs", "mayor-response.json");
    private static readonly JsonSerializerOptions ManualResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public string Name => "Mayor";

    public async Task RunPlayCycle(CancellationToken ct)
    {
        status.Begin();
        string? cycleError = null;
        try
        {
            MayorBriefing briefing                 = briefings.GetMayorBriefing();
            MayorDirectiveSet directiveSet          = rules.Evaluate(briefing, ColonyContext.Default);
            Core.Advice.MayorAgenda? previous      = agendaStore.Current;

            log.LogInformation(
                "Mayor wake briefing_version={BriefingVersion} previous_agenda_version={PrevVersion} directives=[{Directives}]",
                briefing.BriefingVersion, previous?.Version ?? 0, FormatDirectives(directiveSet));

            IReadOnlyList<GuideCitation> retrieved = await retriever.RetrieveAsync(briefing, directiveSet.Directives, ct);

            DumpPrompt(briefing, previous, directiveSet.Directives, retrieved);

            Core.Advice.MayorAgendaInput? input = TryLoadManualResponse();
            if (input is null)
            {
                input = await CallLlmWithRetryAsync(briefing, previous, directiveSet.Directives, retrieved, ct);
                if (input is null)
                {
                    cycleError = "LLM call failed twice";
                    log.LogError("Mayor LLM call failed twice; agenda not updated this turn.");
                    return;
                }
                status.MarkLlmSuccess();
            }

            // Re-attach the guide_citations the retriever produced — the LLM only references them by id.
            input = input with { GuideCitations = retrieved };

            Core.Advice.MayorAgenda stamped = agendaStore.Update(input, FormatTick(briefing));
            bus.Publish(new AgendaUpdated(stamped));

            log.LogInformation(
                "Mayor agenda v{Version} stored and published (tick={Tick} short_term={ShortCount})",
                stamped.Version, stamped.UpdatedInGameTick, stamped.ShortTerm.Count);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                status.End(cycleError);
        }
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;  // M6

    private async Task<Core.Advice.MayorAgendaInput?> CallLlmWithRetryAsync(
        MayorBriefing briefing, Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> directives, IReadOnlyList<GuideCitation> retrieved, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Core.Advice.MayorAgendaInput input = await llm.CallMayorAsync(briefing, previous, directives, retrieved, ct);
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

    /// <summary>
    /// Manual fallback: if logs/mayor-response.json was saved more recently than the
    /// last successful Gemini call, parse it and return as the cycle's input. The user
    /// drops a response from another LLM there when Gemini is rate-limited.
    /// </summary>
    private Core.Advice.MayorAgendaInput? TryLoadManualResponse()
    {
        FileInfo fi = new(ManualResponsePath);
        if (!fi.Exists) return null;

        DateTime cutoff = status.LastLlmSuccessAt?.UtcDateTime ?? DateTime.MinValue;
        if (fi.LastWriteTimeUtc <= cutoff)
        {
            log.LogDebug(
                "Mayor manual response file at {Path} is older than last LLM success ({Cutoff:O}); ignoring",
                ManualResponsePath, cutoff);
            return null;
        }

        try
        {
            string body = File.ReadAllText(ManualResponsePath);
            Core.Advice.MayorAgendaInput? parsed =
                JsonSerializer.Deserialize<Core.Advice.MayorAgendaInput>(body, ManualResponseJson);
            if (parsed is null)
            {
                log.LogWarning("Mayor manual response at {Path} parsed to null; ignoring", ManualResponsePath);
                return null;
            }

            Core.Advice.MayorAgendaInput capped = parsed.ShortTerm.Count > ShortTermCap
                ? parsed with { ShortTerm = parsed.ShortTerm.Take(ShortTermCap).ToList() }
                : parsed;

            log.LogInformation(
                "Mayor using manual response from {Path} (mtime={Mtime:O}, last LLM success={Cutoff:O})",
                ManualResponsePath, fi.LastWriteTimeUtc, cutoff);
            return capped;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "Mayor manual response at {Path} failed to parse — falling through to Gemini",
                ManualResponsePath);
            return null;
        }
    }

    private void DumpPrompt(MayorBriefing briefing, Core.Advice.MayorAgenda? previous,
                            IReadOnlyList<string> directives, IReadOnlyList<GuideCitation> retrieved)
    {
        try
        {
            string system = prompts.MayorSystemPrompt;
            string user   = prompts.BuildMayorUserMessage(briefing, previous, directives, retrieved);
            string body   =
                $"<!-- Mayor prompt snapshot — briefing v{briefing.BriefingVersion}, " +
                $"previous agenda v{previous?.Version ?? 0}, retrieved {retrieved.Count} guides, " +
                $"written {DateTime.UtcNow:O} -->\n\n" +
                $"# system\n\n{system}\n\n# user\n\n{user}\n";
            Directory.CreateDirectory(Path.GetDirectoryName(PromptDumpPath)!);
            File.WriteAllText(PromptDumpPath, body);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to dump Mayor prompt to {Path}", PromptDumpPath);
        }
    }

    private static string FormatTick(MayorBriefing b) =>
        $"Y{b.Date.Year ?? 0}{b.Date.Quadrum ?? "?"}D{b.Date.Day ?? 0}";

    private static string FormatDirectives(MayorDirectiveSet d)
    {
        List<string> fired = new(4);
        if (d.WinterPrepRequired)   fired.Add("winter_prep_required");
        if (d.FoodSecurityCritical) fired.Add("food_security_critical");
        if (d.YearTwoTransition)    fired.Add("year_two_transition");
        if (d.QuietDay && fired.Count == 0) fired.Add("quiet_day");
        return string.Join(',', fired);
    }
}
