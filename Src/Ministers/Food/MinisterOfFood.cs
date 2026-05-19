using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.State;

namespace RimBob.Ministers.Food;

public sealed class MinisterOfFood(
    BriefingCache briefings,
    Rules rules,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    LlmClient llm,
    FoodRagRetriever retriever,
    ILogger<MinisterOfFood> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    public string Name => "Food";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        FoodBriefing briefing = briefings.GetFoodBriefing();
        MinisterBriefingContext context = BuildContext();

        if (cycle.IsBootstrap)
        {
            log.LogInformation("Food bootstrap: forcing first live cycle escalation");
            bool bootstrapped = await RunEscalationAsync(
                cycle,
                briefing,
                context,
                new Escalate(
                    "bootstrap_first_live_cycle",
                    new { briefing.BriefingVersion, briefing.GameTick },
                    RuleTraceDetails.Escalated(
                        "bootstrap_first_live_cycle",
                        "first live Food cycle forces an LLM bootstrap memo")),
                ct);
            if (bootstrapped) return;

            log.LogWarning("Food bootstrap escalation failed; falling back to normal rules evaluation.");
        }

        RulesResult result = rules.Evaluate(briefing, ColonyContext.Default);

        switch (result)
        {
            case Decision decision:
                string ruleStateSummary = FoodStateSummary.Build(briefing);
                AdviceChainModel ruleChain = FoodChainModelBuilder.Build(briefing, decision.Advice);
                PublishSnapshot(decision.Advice, decision.Flags, ruleStateSummary, ruleChain);
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "rules",
                    Briefing: briefing,
                    Context: context,
                    RuleTrace: decision.Trace,
                    RuleDiagnostics: decision.Diagnostics,
                    EscalationReason: null,
                    EscalationContext: null,
                    GuideCitations: null,
                    Advice: decision.Advice,
                    Flags: decision.Flags,
                    StateSummary: ruleStateSummary,
                    Chain: ruleChain), ct);
                log.LogInformation(
                    "Food rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
                    decision.Trace, decision.Advice.Count, decision.Flags.Count);
                break;

            case Escalate escalate:
                await RunEscalationAsync(cycle, briefing, context, escalate, ct);
                break;
        }
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;

    private async Task<bool> RunEscalationAsync(
        PlayCycleContext cycle,
        FoodBriefing briefing,
        MinisterBriefingContext context,
        Escalate escalate,
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
            RuleTraceDetails ruleDiagnostics = DiagnosticsWithLlmEmissions(escalate, response);
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
                "Food escalation reason={Reason} advice={AdviceCount} flags={FlagCount}",
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
                RuleDiagnostics: escalate.Diagnostics,
                EscalationReason: escalate.Reason,
                EscalationContext: escalationContext,
                GuideCitations: citations,
                Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                LlmAttemptStarted: llmAttemptStarted), ct);
            log.LogWarning(ex, "Food escalation failed; no advice emitted this cycle. reason={Reason}", escalate.Reason);
            return false;
        }
    }

    private Task PersistReplayAsync(MinisterReplayEntry entry, CancellationToken ct) =>
        replay?.RecordAsync(entry, ct) ?? Task.CompletedTask;

    private static RuleTraceDetails DiagnosticsWithLlmEmissions(Escalate escalate, FoodLlmResponse response)
    {
        string rule = escalate.Diagnostics?.SelectedRule ?? escalate.Reason;
        RuleTraceDetails diagnostics = escalate.Diagnostics
            ?? RuleTraceDetails.Escalated(rule, escalate.Reason);
        return diagnostics.WithEmissions(
            "llm_after_escalation",
            rule,
            response.Advice,
            response.Flags);
    }

    private void PublishSnapshot(
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> emittedFlags,
        string? stateSummary,
        AdviceChainModel? chain)
    {
        bus.ReplaceMinisterAdvice(Name, advice, stateSummary, chain);
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
