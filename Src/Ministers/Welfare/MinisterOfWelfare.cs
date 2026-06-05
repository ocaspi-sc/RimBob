using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Ministers.Welfare;

public sealed class MinisterOfWelfare(
    BriefingCache briefings,
    Rules rules,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    LlmClient llm,
    WelfareRagRetriever retriever,
    ILogger<MinisterOfWelfare> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    public string Name => "Welfare";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        WelfareSourceBriefing briefing = briefings.GetWelfareBriefing();
        MinisterBriefingContext context = BuildContext(outputStore.CurrentMayorAgenda);

        if (cycle.RunMode == MinisterRunMode.ForceLlm)
        {
            await RunEscalationAsync(
                cycle,
                briefing,
                context,
                new Escalate(
                    "manual_llm_trigger",
                    "dashboard Run LLM confirms Welfare's LLM path",
                    new { briefing.BriefingVersion, briefing.GameTick }),
                RuleTraceDetails.Escalated(
                    "manual_llm_trigger",
                    "dashboard Run LLM confirms Welfare's LLM path"),
                ct);
            return;
        }

        RuleRun result = rules.Evaluate(briefing);
        Escalate? escalation = result.Decisions.OfType<Escalate>().SingleOrDefault();
        if (escalation is not null && result.Decisions.Count == 1)
        {
            string unresolvedSummary = WelfareStateSummary.Build(briefing);
            PublishSnapshot([], [], unresolvedSummary);
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "rules",
                Briefing: briefing,
                Context: context,
                RuleTrace: null,
                RuleDiagnostics: result.Diagnostics,
                EscalationReason: escalation.Reason,
                EscalationContext: escalation.Context,
                GuideCitations: null,
                Advice: [],
                Flags: [],
                StateSummary: unresolvedSummary), ct);
            log.LogInformation(
                "Welfare rules path requested LLM escalation and is awaiting dashboard confirmation. reason={Reason}",
                escalation.Reason);
            return;
        }

        DecisionProjectionContext projectionContext = new(
            Minister: Name,
            Domain: "welfare",
            BriefingVersion: briefing.BriefingVersion,
            GameDate: null,
            GameTick: briefing.GameTick,
            Now: DateTimeOffset.UtcNow);
        IReadOnlyList<AdviceItem> advice = DecisionProjection.ProjectAdvice(result.Decisions, projectionContext);
        IReadOnlyList<AgentFlag> emittedFlags = DecisionProjection.ProjectFlags(result.Decisions, projectionContext);
        string stateSummary = WelfareStateSummary.Build(briefing);
        PublishSnapshot(advice, emittedFlags, stateSummary);
        string? trace = result.Decisions.Count > 0
            ? MinisterRuleTableEvaluator.CompositeTrace(result.Decisions)
            : result.Diagnostics.SelectedRule?.Value;
        await PersistReplayAsync(new MinisterReplayEntry(
            Minister: Name,
            Cycle: cycle,
            Path: "rules",
            Briefing: briefing,
            Context: context,
            RuleTrace: trace,
            RuleDiagnostics: result.Diagnostics,
            Advice: advice,
            Flags: emittedFlags,
            StateSummary: stateSummary), ct);
        log.LogInformation(
            "Welfare rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
            trace,
            advice.Count,
            emittedFlags.Count);
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;

    private async Task<bool> RunEscalationAsync(
        PlayCycleContext cycle,
        WelfareSourceBriefing briefing,
        MinisterBriefingContext context,
        Escalate escalate,
        RuleTraceDetails diagnostics,
        CancellationToken ct)
    {
        DateTimeOffset llmAttemptStarted = DateTimeOffset.UtcNow;
        IReadOnlyList<GuideCitation> citations = [];
        try
        {
            citations = await retriever.RetrieveAsync(briefing, ct);
            WelfareLlmResponse response = await llm.CallWelfareAsync(briefing, context, citations, ct);
            string stateSummary = WelfareStateSummary.Build(briefing);
            RuleTraceDetails ruleDiagnostics = DiagnosticsWithLlmEmissions(escalate, diagnostics);
            PublishSnapshot(response.Advice, response.Flags, stateSummary);
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "llm",
                Briefing: briefing,
                Context: context,
                RuleTrace: null,
                RuleDiagnostics: ruleDiagnostics,
                EscalationReason: escalate.Reason,
                EscalationContext: escalate.Context,
                GuideCitations: citations,
                Advice: response.Advice,
                Flags: response.Flags,
                StateSummary: stateSummary,
                LlmAttemptStarted: llmAttemptStarted,
                OutputKind: "advice_flags",
                Output: new { advice = response.Advice, flags = response.Flags, notes = response.Notes }), ct);
            log.LogInformation(
                "Welfare escalation reason={Reason} advice={AdviceCount} flags={FlagCount}",
                escalate.Reason, response.Advice.Count, response.Flags.Count);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "llm_failed",
                Briefing: briefing,
                Context: context,
                RuleTrace: null,
                RuleDiagnostics: diagnostics,
                EscalationReason: escalate.Reason,
                EscalationContext: escalate.Context,
                GuideCitations: citations,
                Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                LlmAttemptStarted: llmAttemptStarted), ct);
            log.LogWarning(ex, "Welfare escalation failed; no advice emitted this cycle. reason={Reason}", escalate.Reason);
            return false;
        }
    }

    public static MinisterBriefingContext BuildContext(MayorAgenda? agenda)
    {
        if (agenda is null) return MinisterBriefingContext.Empty;

        string? direction = agenda.CabinetDirection.TryGetValue("welfare", out string? exact)
            ? exact
            : agenda.CabinetDirection.TryGetValue("Welfare", out string? titleCase)
                ? titleCase
                : agenda.CabinetDirection.TryGetValue("mood", out string? mood)
                    ? mood
                    : null;

        IReadOnlyList<string> domains = agenda.ShortTerm
            .Where(priority => priority.Status == AgendaPriorityStatus.Active)
            .Select(priority => InferDomain(priority.Text))
            .Where(domain => domain is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MinisterBriefingContext(agenda.Posture, direction, domains);
    }

    private Task PersistReplayAsync(MinisterReplayEntry entry, CancellationToken ct) =>
        replay?.RecordAsync(entry, ct) ?? Task.CompletedTask;

    private static RuleTraceDetails DiagnosticsWithLlmEmissions(Escalate escalate, RuleTraceDetails diagnostics)
    {
        return diagnostics.AllRules.Count == 0
            ? RuleTraceDetails.Escalated(escalate.Rule, escalate.Reason)
            : diagnostics;
    }

    private void PublishSnapshot(
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> emittedFlags,
        string stateSummary)
    {
        bus.ReplaceMinisterAdvice(Name, advice, stateSummary, flags: emittedFlags);
        foreach (AgentFlag flag in emittedFlags)
            flags.Publish(flag);
    }

    private static string? InferDomain(string text)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("mood") ||
            lower.Contains("break") ||
            lower.Contains("recreation") ||
            lower.Contains("comfort") ||
            lower.Contains("beauty") ||
            lower.Contains("sleep") ||
            lower.Contains("bed"))
        {
            return "welfare";
        }

        if (lower.Contains("food") || lower.Contains("meal") || lower.Contains("harvest"))
            return "food";
        if (lower.Contains("build") || lower.Contains("power") || lower.Contains("room"))
            return "construction";
        if (lower.Contains("defense") || lower.Contains("raid"))
            return "defense";
        if (lower.Contains("research"))
            return "research";
        return null;
    }
}
