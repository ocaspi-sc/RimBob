using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Ministers.Food;

public sealed class Chef(
    BriefingCache briefings,
    Rules rules,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    LlmClient llm,
    FoodRagRetriever retriever,
    ILogger<Chef> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    public string Name => "Chef";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        FoodBriefing briefing = briefings.GetFoodBriefing();
        MinisterBriefingContext context = BuildContext();

        if (cycle.RunMode == MinisterRunMode.ForceLlm)
        {
            await RunEscalationAsync(
                cycle,
                briefing,
                context,
                new Escalate(
                    "manual_llm_trigger",
                    "dashboard Run LLM forces Chef's LLM path",
                    new { briefing.BriefingVersion, briefing.GameTick }),
                RuleTraceDetails.Escalated(
                    "manual_llm_trigger",
                    "dashboard Run LLM forces Chef's LLM path"),
                ct);
            return;
        }

        if (cycle.IsBootstrap)
        {
            log.LogInformation("Chef bootstrap: forcing first live cycle escalation");
            bool bootstrapped = await RunEscalationAsync(
                cycle,
                briefing,
                context,
                new Escalate(
                    "bootstrap_first_live_cycle",
                    "first live Chef cycle forces an LLM bootstrap memo",
                    new { briefing.BriefingVersion, briefing.GameTick }),
                RuleTraceDetails.Escalated(
                    "bootstrap_first_live_cycle",
                    "first live Chef cycle forces an LLM bootstrap memo"),
                ct);
            if (bootstrapped) return;

            log.LogWarning("Chef bootstrap escalation failed; falling back to normal rules evaluation.");
        }

        RuleRun result = rules.Evaluate(briefing, ColonyContext.Default);
        Escalate? escalation = result.Decisions.OfType<Escalate>().SingleOrDefault();
        if (escalation is not null && result.Decisions.Count == 1)
        {
            if (cycle.RunMode == MinisterRunMode.RulesOnly)
            {
                string unresolvedSummary = FoodStateSummary.Build(briefing);
                AdviceChainModel emptyChain = FoodChainModelBuilder.Build(briefing, []);
                PublishSnapshot([], [], unresolvedSummary, emptyChain);
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "rules",
                    Briefing: briefing,
                    Context: context,
                    RuleTrace: null,
                    RuleDiagnostics: result.Diagnostics,
                    EscalationReason: escalation.Reason,
                    EscalationContext: BuildEscalationContext(escalation.Context, BuildCropCandidates(briefing)),
                    GuideCitations: null,
                    Advice: [],
                    Flags: [],
                    StateSummary: unresolvedSummary,
                    Chain: emptyChain), ct);
                log.LogInformation("Chef rules-only trigger stopped before LLM escalation. reason={Reason}", escalation.Reason);
                return;
            }

            await RunEscalationAsync(cycle, briefing, context, escalation, result.Diagnostics, ct);
            return;
        }

        DecisionProjectionContext projectionContext = new(
            Minister: Name,
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameDate: briefing.Date,
            GameTick: briefing.GameTick,
            Now: DateTimeOffset.UtcNow);
        IReadOnlyList<AdviceItem> advice = DecisionProjection.ProjectAdvice(result.Decisions, projectionContext);
        IReadOnlyList<AgentFlag> emittedFlags = DecisionProjection.ProjectFlags(result.Decisions, projectionContext);
        string ruleStateSummary = FoodStateSummary.Build(briefing);
        AdviceChainModel ruleChain = FoodChainModelBuilder.Build(briefing, advice);
        PublishSnapshot(advice, emittedFlags, ruleStateSummary, ruleChain);
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
            EscalationReason: null,
            EscalationContext: null,
            GuideCitations: null,
            Advice: advice,
            Flags: emittedFlags,
            StateSummary: ruleStateSummary,
            Chain: ruleChain), ct);
        log.LogInformation(
            "Chef rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
            trace, advice.Count, emittedFlags.Count);
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;

    private async Task<bool> RunEscalationAsync(
        PlayCycleContext cycle,
        FoodBriefing briefing,
        MinisterBriefingContext context,
        Escalate escalate,
        RuleTraceDetails diagnostics,
        CancellationToken ct)
    {
        DateTimeOffset llmAttemptStarted = DateTimeOffset.UtcNow;
        IReadOnlyList<GuideCitation> citations = [];
        IReadOnlyList<FoodPromptCropCandidate> cropCandidates = BuildCropCandidates(briefing);
        object? escalationContext = BuildEscalationContext(escalate.Context, cropCandidates);
        try
        {
            citations = await retriever.RetrieveAsync(briefing, ct);
            FoodLlmResponse response = await llm.CallFoodAsync(briefing, context, citations, cropCandidates, ct);
            string stateSummary = FoodStateSummary.Build(briefing);
            AdviceChainModel chain = FoodChainModelBuilder.Build(briefing, response.Advice);
            RuleTraceDetails ruleDiagnostics = DiagnosticsWithLlmEmissions(escalate, diagnostics);
            PublishSnapshot(response.Advice, response.Flags, stateSummary, chain);
            await PersistReplayAsync(new MinisterReplayEntry(
                Minister: Name,
                Cycle: cycle,
                Path: "llm",
                Briefing: briefing,
                Context: context,
                RuleTrace: null,
                RuleDiagnostics: ruleDiagnostics,
                EscalationReason: escalate.Reason,
                EscalationContext: escalationContext,
                GuideCitations: citations,
                Advice: response.Advice,
                Flags: response.Flags,
                StateSummary: stateSummary,
                Chain: chain,
                LlmAttemptStarted: llmAttemptStarted,
                OutputKind: "advice_flags",
                Output: new { advice = response.Advice, flags = response.Flags }), ct);
            log.LogInformation(
                "Chef escalation reason={Reason} advice={AdviceCount} flags={FlagCount}",
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
                EscalationContext: escalationContext,
                GuideCitations: citations,
                Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                LlmAttemptStarted: llmAttemptStarted), ct);
            log.LogWarning(ex, "Chef escalation failed; no advice emitted this cycle. reason={Reason}", escalate.Reason);
            return false;
        }
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
        string? stateSummary,
        AdviceChainModel? chain)
    {
        bus.ReplaceMinisterAdvice(Name, advice, stateSummary, chain, emittedFlags);
        foreach (AgentFlag flag in emittedFlags)
            flags.Publish(flag);
    }

    private MinisterBriefingContext BuildContext() => BuildContext(outputStore.CurrentMayorAgenda);

    public static IReadOnlyList<FoodPromptCropCandidate> BuildCropCandidates(FoodBriefing briefing) =>
        FoodCropMath.Recommend(briefing).Candidates
            .Take(3)
            .Select(candidate => new FoodPromptCropCandidate(
                CropDef: candidate.CropDef,
                Label: candidate.Label,
                Tiles: candidate.Tiles,
                HarvestNutrition: candidate.HarvestNutrition,
                GrowDays: candidate.GrowDays,
                ProjectedDaysAdded: candidate.ProjectedDaysAdded,
                FitsSeason: candidate.FitsSeason,
                DaysToWinterMargin: candidate.DaysToWinterMargin,
                ClassificationConfidence: candidate.ClassificationConfidence,
                StorageMultiplier: candidate.StorageMultiplier,
                Reason: candidate.Reason))
            .ToList();

    private static object? BuildEscalationContext(
        object? ruleContext,
        IReadOnlyList<FoodPromptCropCandidate> cropCandidates) =>
        cropCandidates.Count == 0
            ? ruleContext
            : new FoodEscalationContext(ruleContext, cropCandidates);

    public static MinisterBriefingContext BuildContext(MayorAgenda? agenda)
    {
        if (agenda is null) return MinisterBriefingContext.Empty;

        string? direction = agenda.CabinetDirection.TryGetValue("food", out string? exact)
            ? exact
            : agenda.CabinetDirection.TryGetValue("Chef", out string? chef)
                ? chef
                : agenda.CabinetDirection.TryGetValue("Food", out string? titleCase)
                    ? titleCase
                    : null;

        IReadOnlyList<string> domains = agenda.ShortTerm
            .Where(p => p.Status == AgendaPriorityStatus.Active)
            .Select(p => InferDomain(p.Text))
            .Where(d => d is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MinisterBriefingContext(agenda.Posture, direction, domains);
    }

    private static string? InferDomain(string text)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("food") || lower.Contains("meal") || lower.Contains("harvest") || lower.Contains("freezer"))
            return "food";
        if (lower.Contains("defense") || lower.Contains("raid") || lower.Contains("wall"))
            return "defense";
        if (lower.Contains("mood") || lower.Contains("recreation") || lower.Contains("hospital"))
            return "welfare";
        if (lower.Contains("build") || lower.Contains("power") || lower.Contains("room"))
            return "construction";
        if (lower.Contains("research"))
            return "research";
        return null;
    }

    private sealed record FoodEscalationContext(
        object? RuleContext,
        IReadOnlyList<FoodPromptCropCandidate> CropCandidates);
}
