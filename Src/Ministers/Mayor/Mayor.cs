using System.Text.Json;
using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Ministers.Mayor;

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
    FlagChannel         flags,
    ILogger<Mayor>      log,
    MinisterReplayRecorder? replay = null,
    string? manualResponsePath = null
) : IMinister
{
    private const int ShortTermCap = 5;
    private static readonly string PromptDumpPath     = Path.Combine("logs", "mayor-prompt-latest.md");
    private static readonly string DefaultManualResponsePath = Path.Combine("logs", "mayor-response.json");
    private static readonly JsonSerializerOptions ManualResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly string _manualResponsePath = manualResponsePath ?? DefaultManualResponsePath;

    public string Name => "Mayor";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        status.Begin();
        string? cycleError = null;
        try
        {
            MayorBriefing briefing                 = briefings.GetMayorBriefing();
            IReadOnlyList<AgentFlag> activeFlags    = flags.Active(FlagSeverity.Medium);
            MayorDirectiveSet directiveSet          = rules.Evaluate(briefing, ColonyContext.Default, activeFlags);
            Core.Advice.MayorAgenda? previous      = agendaStore.Current;

            log.LogInformation(
                "Mayor wake briefing_version={BriefingVersion} previous_agenda_version={PrevVersion} directives=[{Directives}]",
                briefing.BriefingVersion, previous?.Version ?? 0, FormatDirectives(directiveSet));

            IReadOnlyList<GuideCitation> retrieved = await retriever.RetrieveAsync(briefing, directiveSet.Directives, ct);

            DumpPrompt(briefing, previous, directiveSet.Directives, retrieved, activeFlags);

            Core.Advice.MayorAgendaInput? input = await TryLoadManualResponseAsync(
                cycle,
                briefing,
                previous,
                directiveSet.Directives,
                retrieved,
                activeFlags,
                ct);
            if (input is null)
            {
                input = await CallLlmWithRetryAsync(
                    cycle,
                    briefing,
                    previous,
                    directiveSet.Directives,
                    retrieved,
                    activeFlags,
                    ct);
                if (input is null)
                {
                    cycleError = "LLM call failed twice";
                    if (previous is not null)
                    {
                        log.LogError("Mayor LLM call failed twice; agenda not updated this turn.");
                        return;
                    }

                    input = MayorAgendaBootstrap.Build(
                        briefing,
                        directiveSet.Directives,
                        activeFlags,
                        "Mayor LLM failed twice before any agenda was persisted.");
                    log.LogWarning("Mayor LLM failed twice with no previous agenda; publishing bootstrap agenda.");
                }
                else
                {
                    status.MarkLlmSuccess();
                }
            }

            // Re-attach the guide_citations the retriever produced — the LLM only references them by id.
            input = input with { GuideCitations = retrieved };

            Core.Advice.MayorAgenda stamped;
            try
            {
                stamped = await agendaStore.UpdateAsync(input, FormatTick(briefing), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                cycleError = "Agenda persistence failed";
                log.LogError(ex, "Mayor agenda persistence failed; agenda not published this turn.");
                throw;
            }

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
        PlayCycleContext cycle,
        MayorBriefing briefing, Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> directives, IReadOnlyList<GuideCitation> retrieved,
        IReadOnlyList<AgentFlag> activeFlags, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            DateTimeOffset llmAttemptStarted = DateTimeOffset.UtcNow;
            try
            {
                Core.Advice.MayorAgendaInput input = await llm.CallMayorAsync(briefing, previous, directives, retrieved, activeFlags, ct);
                if (input.ShortTerm.Count > ShortTermCap)
                {
                    log.LogWarning(
                        "Mayor agenda has {Count} short_term items (cap is {Cap}); attempt {Attempt}",
                        input.ShortTerm.Count, ShortTermCap, attempt);
                    if (attempt == 2)
                    {
                        // Truncate rather than drop the whole agenda after the second try.
                        Core.Advice.MayorAgendaInput capped = input with { ShortTerm = input.ShortTerm.Take(ShortTermCap).ToList() };
                        await PersistReplayAsync(new MinisterReplayEntry(
                            Minister: Name,
                            Cycle: cycle,
                            Path: "llm",
                            Briefing: briefing,
                            Context: BuildMayorReplayContext(previous, directives, activeFlags),
                            EscalationReason: "mayor_agenda_update",
                            EscalationContext: new
                            {
                                attempt,
                                short_term_cap = ShortTermCap,
                                short_term_count = input.ShortTerm.Count,
                                truncated = true
                            },
                            GuideCitations: retrieved,
                            LlmAttemptStarted: llmAttemptStarted,
                            OutputKind: "mayor_agenda_input",
                            Output: capped), ct);
                        return capped;
                    }
                    await PersistReplayAsync(new MinisterReplayEntry(
                        Minister: Name,
                        Cycle: cycle,
                        Path: "llm_failed",
                        Briefing: briefing,
                        Context: BuildMayorReplayContext(previous, directives, activeFlags),
                        EscalationReason: "mayor_agenda_update",
                        EscalationContext: new
                        {
                            attempt,
                            short_term_cap = ShortTermCap,
                            short_term_count = input.ShortTerm.Count,
                            validation = "short_term_over_cap",
                            retrying = true
                        },
                        GuideCitations: retrieved,
                        Error: new ReplayErrorSummary(
                            "ValidationRejected",
                            $"Mayor agenda short_term count {input.ShortTerm.Count} exceeded cap {ShortTermCap}."),
                        LlmAttemptStarted: llmAttemptStarted,
                        OutputKind: "mayor_agenda_input",
                        Output: input), ct);
                    continue;
                }
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "llm",
                    Briefing: briefing,
                    Context: BuildMayorReplayContext(previous, directives, activeFlags),
                    EscalationReason: "mayor_agenda_update",
                    EscalationContext: new
                    {
                        attempt,
                        short_term_cap = ShortTermCap,
                        short_term_count = input.ShortTerm.Count
                    },
                    GuideCitations: retrieved,
                    LlmAttemptStarted: llmAttemptStarted,
                    OutputKind: "mayor_agenda_input",
                    Output: input), ct);
                return input;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "llm_failed",
                    Briefing: briefing,
                    Context: BuildMayorReplayContext(previous, directives, activeFlags),
                    EscalationReason: "mayor_agenda_update",
                    EscalationContext: new
                    {
                        attempt,
                        short_term_cap = ShortTermCap
                    },
                    GuideCitations: retrieved,
                    Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                    LlmAttemptStarted: llmAttemptStarted), ct);
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
    private async Task<Core.Advice.MayorAgendaInput?> TryLoadManualResponseAsync(
        PlayCycleContext cycle,
        MayorBriefing briefing,
        Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> directives,
        IReadOnlyList<GuideCitation> retrieved,
        IReadOnlyList<AgentFlag> activeFlags,
        CancellationToken ct)
    {
        FileInfo fi = new(_manualResponsePath);
        if (!fi.Exists) return null;

        DateTime cutoff = status.LastLlmSuccessAt?.UtcDateTime ?? DateTime.MinValue;
        if (fi.LastWriteTimeUtc <= cutoff)
        {
            log.LogDebug(
                "Mayor manual response file at {Path} is older than last LLM success ({Cutoff:O}); ignoring",
                _manualResponsePath, cutoff);
            return null;
        }

        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        string body = "";
        int systemPromptChars = 0;
        int userPromptChars = 0;
        try
        {
            body = await File.ReadAllTextAsync(_manualResponsePath, ct);
            (systemPromptChars, userPromptChars) = ManualPromptLengths(
                briefing,
                previous,
                directives,
                retrieved,
                activeFlags);
            Core.Advice.MayorAgendaInput? parsed =
                JsonSerializer.Deserialize<Core.Advice.MayorAgendaInput>(body, ManualResponseJson);
            if (parsed is null)
            {
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "llm_failed",
                    Briefing: briefing,
                    Context: BuildMayorReplayContext(previous, directives, activeFlags),
                    EscalationReason: "manual_llm_response_file",
                    EscalationContext: new
                    {
                        source_path = _manualResponsePath,
                        short_term_cap = ShortTermCap
                    },
                    GuideCitations: retrieved,
                    Error: new ReplayErrorSummary("JsonNull", "Mayor manual response parsed to null."),
                    Llm: ManualLlmMetadata("ManualFile", _manualResponsePath, capturedAt, systemPromptChars, userPromptChars,
                        "manual_parse_failed", body)), ct);
                log.LogWarning("Mayor manual response at {Path} parsed to null; ignoring", _manualResponsePath);
                return null;
            }

            Core.Advice.MayorAgendaInput capped = parsed.ShortTerm.Count > ShortTermCap
                ? parsed with { ShortTerm = parsed.ShortTerm.Take(ShortTermCap).ToList() }
                : parsed;
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "llm",
                Briefing: briefing,
                Context: BuildMayorReplayContext(previous, directives, activeFlags),
                EscalationReason: "manual_llm_response_file",
                EscalationContext: new
                {
                    source_path = _manualResponsePath,
                    short_term_cap = ShortTermCap,
                    short_term_count = parsed.ShortTerm.Count,
                    truncated = parsed.ShortTerm.Count > ShortTermCap
                },
                GuideCitations: retrieved,
                Llm: ManualLlmMetadata("ManualFile", _manualResponsePath, capturedAt, systemPromptChars, userPromptChars,
                    "manual_parsed", body),
                OutputKind: "mayor_agenda_input",
                Output: capped), ct);

            log.LogInformation(
                "Mayor using manual response from {Path} (mtime={Mtime:O}, last LLM success={Cutoff:O})",
                _manualResponsePath, fi.LastWriteTimeUtc, cutoff);
            return capped;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "llm_failed",
                Briefing: briefing,
                Context: BuildMayorReplayContext(previous, directives, activeFlags),
                EscalationReason: "manual_llm_response_file",
                EscalationContext: new
                {
                    source_path = _manualResponsePath,
                    short_term_cap = ShortTermCap
                },
                GuideCitations: retrieved,
                Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                Llm: ManualLlmMetadata("ManualFile", _manualResponsePath, capturedAt, systemPromptChars, userPromptChars,
                    "manual_parse_failed", body)), ct);
            log.LogWarning(ex,
                "Mayor manual response at {Path} failed to parse - falling through to Gemini",
                _manualResponsePath);
            return null;
        }
    }

    private Task PersistReplayAsync(MinisterReplayEntry entry, CancellationToken ct) =>
        replay?.RecordAsync(entry, ct) ?? Task.CompletedTask;

    private static object BuildMayorReplayContext(
        Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> directives,
        IReadOnlyList<AgentFlag> activeFlags) =>
        new
        {
            previous_agenda_version = previous?.Version,
            directives,
            active_flags = activeFlags
        };

    private (int SystemPromptChars, int UserPromptChars) ManualPromptLengths(
        MayorBriefing briefing,
        Core.Advice.MayorAgenda? previous,
        IReadOnlyList<string> directives,
        IReadOnlyList<GuideCitation> retrieved,
        IReadOnlyList<AgentFlag> activeFlags)
    {
        try
        {
            string system = prompts.MayorSystemPrompt;
            string user = prompts.BuildMayorUserMessage(briefing, previous, directives, retrieved, activeFlags);
            return (system.Length, user.Length);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Failed to capture Mayor manual response prompt lengths for replay metadata.");
            return (0, 0);
        }
    }

    private static ReplayLlmMetadata ManualLlmMetadata(
        string provider,
        string model,
        DateTimeOffset capturedAt,
        int systemPromptChars,
        int userPromptChars,
        string status,
        string rawOutput) =>
        new(
            Provider: provider,
            Model: model,
            CapturedAt: capturedAt,
            SystemPromptChars: systemPromptChars,
            UserPromptChars: userPromptChars,
            Status: status,
            ParseMode: "manual_json",
            LatencyMs: 0,
            RawOutput: rawOutput);

    private void DumpPrompt(MayorBriefing briefing, Core.Advice.MayorAgenda? previous,
                            IReadOnlyList<string> directives, IReadOnlyList<GuideCitation> retrieved,
                            IReadOnlyList<AgentFlag> activeFlags)
    {
        try
        {
            string system = prompts.MayorSystemPrompt;
            string user   = prompts.BuildMayorUserMessage(briefing, previous, directives, retrieved, activeFlags);
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
