using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Ministers.Willie;

public sealed class MinisterOfWillie(
    BriefingCache briefings,
    Rules rules,
    IPlacementSolver placementSolver,
    ColonyState colonyState,
    WillieSolverStore solverStore,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    ILogger<MinisterOfWillie> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    public string Name => "Willie";

    public async Task RunPlayCycle(PlayCycleContext cycle, CancellationToken ct)
    {
        WillieBriefing briefing = briefings.GetWillieBriefing();
        MinisterBriefingContext context = BuildContext(outputStore.CurrentMayorAgenda);
        IReadOnlyList<AgentFlag> activeFlags = flags.Active();
        IReadOnlyList<BuildingRequest> inboundRequests = ActiveWillieBuildingRequests(activeFlags, cycle.Flag);

        RulesResult result = rules.Evaluate(briefing, ColonyContext.Default, inboundRequests);
        switch (result)
        {
            case Decision decision:
                PlacementSolverReplayOutput? placementReplayOutput = null;
                IReadOnlyList<AdviceItem> advice = decision.Advice;
                if (Rules.TryGetPlacementRequest(decision.Trace, briefing, inboundRequests, out BuildingRequest placementRequest))
                {
                    PlacementSolveAttempt attempt = await TrySolvePlacementAsync(placementRequest, briefing, ct);
                    advice = EnrichSolverAdvice(
                        advice,
                        attempt,
                        DrivingAdviceId(decision.Trace),
                        removeFallbackWhenApplyReady: IsMissingRoomTrace(decision.Trace));
                    placementReplayOutput = attempt.ReplayOutput;
                    solverStore.Record(new WillieSolverSnapshot(
                        Minister: Name,
                        Request: WillieSolverRequestSnapshot.FromRequest(
                            placementRequest,
                            SourceMinisterForRequest(placementRequest, activeFlags, cycle.Flag)),
                        GameTick: briefing.GameTick,
                        CapturedAt: DateTimeOffset.UtcNow,
                        Output: attempt.ReplayOutput));
                }

                string stateSummary = WillieStateSummary.Build(briefing);
                RuleTraceDetails? diagnostics = decision.Diagnostics?.WithEmissions("rules", decision.Trace, advice, decision.Flags);
                PublishSnapshot(advice, decision.Flags, stateSummary);
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "rules",
                    Briefing: briefing,
                    Context: context,
                    RuleTrace: decision.Trace,
                    RuleDiagnostics: diagnostics,
                    Advice: advice,
                    Flags: decision.Flags,
                    StateSummary: stateSummary,
                    OutputKind: placementReplayOutput is null ? null : "placement_solver",
                    Output: placementReplayOutput), ct);
                log.LogInformation(
                    "Willie rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
                    decision.Trace, advice.Count, decision.Flags.Count);
                break;

            case Escalate escalate:
                string unresolvedSummary = WillieStateSummary.Build(briefing);
                PublishSnapshot([], [], unresolvedSummary);
                await PersistReplayAsync(new MinisterReplayEntry(
                    Minister: Name,
                    Cycle: cycle,
                    Path: "rules",
                    Briefing: briefing,
                    Context: context,
                    RuleTrace: null,
                    RuleDiagnostics: escalate.Diagnostics,
                    EscalationReason: escalate.Reason,
                    Advice: [],
                    Flags: [],
                    StateSummary: unresolvedSummary), ct);
                log.LogInformation("Willie rules had no deterministic decision. reason={Reason}", escalate.Reason);
                break;
        }
    }

    public Task RunRefinement(CancellationToken ct) => Task.CompletedTask;

    public static MinisterBriefingContext BuildContext(MayorAgenda? agenda)
    {
        if (agenda is null) return MinisterBriefingContext.Empty;

        string? direction = agenda.CabinetDirection.TryGetValue("willie", out string? exact)
            ? exact
            : agenda.CabinetDirection.TryGetValue("Willie", out string? titleCase)
                ? titleCase
                : agenda.CabinetDirection.TryGetValue("construction", out string? construction)
                    ? construction
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

    private static IReadOnlyList<BuildingRequest> ActiveWillieBuildingRequests(
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        List<AgentFlag> flagsToRead = [.. activeFlags];
        if (directFlag is not null &&
            flagsToRead.All(flag => !string.Equals(flag.Id, directFlag.Id, StringComparison.OrdinalIgnoreCase)))
        {
            flagsToRead.Add(directFlag);
        }

        return flagsToRead
            .SelectMany(flag => flag.BuildingRequests ?? [])
            .Where(IsRequestedFromWillie)
            .ToList();
    }

    private static bool IsRequestedFromWillie(BuildingRequest request) =>
        string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase);

    private static string? SourceMinisterForRequest(
        BuildingRequest request,
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        List<AgentFlag> flagsToRead = [.. activeFlags];
        if (directFlag is not null &&
            flagsToRead.All(flag => !string.Equals(flag.Id, directFlag.Id, StringComparison.OrdinalIgnoreCase)))
        {
            flagsToRead.Add(directFlag);
        }

        return flagsToRead.FirstOrDefault(flag =>
                (flag.BuildingRequests ?? []).Any(candidate =>
                    IsRequestedFromWillie(candidate) && candidate == request))
            ?.SourceMinister;
    }

    private async Task<PlacementSolveAttempt> TrySolvePlacementAsync(
        BuildingRequest request,
        WillieBriefing briefing,
        CancellationToken ct)
    {
        PlacementSpec spec = PlacementSpec.FromBuildingRequest(
            request,
            MaterialsOnHandFromStoredResources());
        try
        {
            PlacementResult result = await placementSolver.SolveAsync(spec, briefing, colonyState, ct);
            return PlacementSolveAttempt.FromResult(result, request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Willie placement solver failed for request={Request}", request.Request);
            return PlacementSolveAttempt.FromFailure(ex);
        }
    }

    private IReadOnlyList<MaterialHint>? MaterialsOnHandFromStoredResources()
    {
        IReadOnlyDictionary<string, int> countByDef = colonyState.StoredResources.Value.CountByDef;
        if (countByDef.Count == 0) return null;

        return countByDef
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MaterialHint(pair.Key, pair.Value))
            .ToList();
    }

    private static IReadOnlyList<AdviceItem> EnrichSolverAdvice(
        IReadOnlyList<AdviceItem> advice,
        PlacementSolveAttempt attempt,
        string drivingAdviceId,
        bool removeFallbackWhenApplyReady)
    {
        if (advice.Count == 0) return advice;

        return advice
            .Select(item => IsDrivingAdvice(item, drivingAdviceId)
                ? EnrichAdviceItem(item, attempt, removeFallbackWhenApplyReady)
                : item)
            .ToList();
    }

    private static bool IsDrivingAdvice(AdviceItem item, string drivingAdviceId) =>
        string.Equals(item.Id, drivingAdviceId, StringComparison.OrdinalIgnoreCase);

    private static AdviceItem EnrichAdviceItem(
        AdviceItem item,
        PlacementSolveAttempt attempt,
        bool removeFallbackWhenApplyReady)
    {
        if (attempt.Result is null)
            return item with { Rationale = AppendPlacementNote(item.Rationale, attempt.Note) };

        PlacementResult result = attempt.Result;
        IReadOnlyList<AdviceOption>? options = result.Options.Count == 0
            ? item.Options
            : result.Options.Select(option => option with
                {
                    Readiness = new AdviceOptionReadiness(
                        Draftable: ReadinessWire(result.Draftable),
                        PlacementValid: ReadinessWire(result.PlacementValid),
                        MaterialsReady: ReadinessWire(result.MaterialsReady),
                        ApplyReady: ReadinessWire(result.ApplyReady))
                })
                .ToList();

        bool attachApplyActions = options is { Count: > 0 } &&
            result.ApplyReady != PlacementReadiness.Blocked;
        IReadOnlyList<AdviceAction> baseActions = removeFallbackWhenApplyReady && attachApplyActions
            ? item.Actions.Where(action => action.Kind != AdviceActionKind.PlaceBlueprint || action.Apply is not null).ToList()
            : item.Actions;
        IReadOnlyList<AdviceAction> actions = attachApplyActions
            ? baseActions.Concat(options!.Select(ApplyActionForOption)).ToList()
            : baseActions;

        return item with
        {
            Options = options,
            Actions = actions,
            Rationale = AppendPlacementNote(item.Rationale, attempt.Note)
        };
    }

    private static AdviceAction ApplyActionForOption(AdviceOption option) =>
        new(
            AdviceActionKind.PlaceBlueprint,
            $"Place the {option.Label} blueprint group.",
            Owner: "Willie",
            Apply: new PlaceBlueprintGroupApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                BlueprintGroup: option.BlueprintGroup,
                AssetCount: option.BlueprintGroup.Assets.Count));

    private static string AppendPlacementNote(string rationale, string note) =>
        string.IsNullOrWhiteSpace(rationale) ? note : $"{rationale} {note}";

    private static string ReadinessWire(PlacementReadiness readiness) =>
        readiness.ToString().ToLowerInvariant();

    private static string NoteFor(PlacementResult result, BuildingRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Placement solver: {result.Options.Count} validated option{Plural(result.Options.Count)}; materials_ready={ReadinessWire(result.MaterialsReady)}, apply_ready={ReadinessWire(result.ApplyReady)}.";
        }

        string reason = result.NoFit is null
            ? $"no validated {RequestRoomLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Placement solver no-fit: {reason}.";
    }

    private static string NoFitNote(NoFitReason reason, BuildingRequest request) => reason switch
    {
        NoFitReason.NoAnchors => $"no {RequestAnchorLabel(request)} anchor is available in the Willie briefing",
        NoFitReason.NoDrafts => $"no {RequestRoomLabel(request)} drafts were generated",
        NoFitReason.HardGateRejected => $"all {RequestRoomLabel(request)} drafts failed shared hard gates",
        NoFitReason.NoReachablePath => $"no walkable route to a {RequestAnchorLabel(request)} anchor",
        NoFitReason.ValidationRejected => $"no buildable footprint near the {RequestAnchorLabel(request)} passed fork validation",
        _ => $"no validated {RequestRoomLabel(request)} option was emitted"
    };

    private static string DrivingAdviceId(string trace) =>
        $"willie_{trace}";

    private static bool IsMissingRoomTrace(string trace) =>
        trace is "kitchen_missing" or "hospital_missing" or "storage_room_missing";

    private static string RequestRoomLabel(BuildingRequest request) =>
        request.RoomClass is not null
            ? FormatEnum(request.RoomClass.Value.ToString())
            : FormatEnum(request.TargetClass.ToString());

    private static string RequestAnchorLabel(BuildingRequest request)
    {
        string? target = request.Adjacency?
            .FirstOrDefault(adjacency => adjacency.Relation == AdjacencyRelation.Near)
            ?.Target;
        if (string.IsNullOrWhiteSpace(target) && Rules.IsFreezingBuildRequest(request))
            return "kitchen";

        return string.IsNullOrWhiteSpace(target)
            ? "requested"
            : target.Replace('_', ' ').ToLowerInvariant();
    }

    private static string FormatEnum(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add(' ');
            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static string? InferDomain(string text)
    {
        string lower = text.ToLowerInvariant();
        if (lower.Contains("build") ||
            lower.Contains("power") ||
            lower.Contains("room") ||
            lower.Contains("freezer") ||
            lower.Contains("storage") ||
            lower.Contains("wall"))
        {
            return "construction";
        }

        if (lower.Contains("food") || lower.Contains("meal") || lower.Contains("harvest"))
            return "food";
        if (lower.Contains("defense") || lower.Contains("raid"))
            return "defense";
        if (lower.Contains("mood") || lower.Contains("recreation") || lower.Contains("hospital"))
            return "welfare";
        if (lower.Contains("research"))
            return "research";
        return null;
    }

    private Task PersistReplayAsync(MinisterReplayEntry entry, CancellationToken ct) =>
        replay?.RecordAsync(entry, ct) ?? Task.CompletedTask;

    private void PublishSnapshot(
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> emittedFlags,
        string stateSummary)
    {
        bus.ReplaceMinisterAdvice(Name, advice, stateSummary, flags: emittedFlags);
        foreach (AgentFlag flag in emittedFlags)
            flags.Publish(flag);
    }

    private sealed record PlacementSolveAttempt(
        PlacementResult? Result,
        string Note,
        PlacementSolverReplayOutput ReplayOutput)
    {
        public static PlacementSolveAttempt FromResult(PlacementResult result, BuildingRequest request) =>
            new(
                result,
                NoteFor(result, request),
                PlacementSolverReplayOutput.FromResult(result));

        public static PlacementSolveAttempt FromFailure(Exception ex)
        {
            string errorType = ex.GetType().Name;
            string note = $"Placement solver unavailable: {errorType}. Keeping prose advice.";
            return new PlacementSolveAttempt(
                null,
                note,
                PlacementSolverReplayOutput.FromFailure(errorType, ex.Message));
        }
    }
}

public sealed record PlacementSolverReplayOutput(
    string Status,
    string? NoFit,
    string? Draftable,
    string? PlacementValid,
    string? MaterialsReady,
    string? ApplyReady,
    PlacementTrace? Trace,
    string? ErrorType,
    string? ErrorMessage)
{
    public static PlacementSolverReplayOutput FromResult(PlacementResult result) =>
        new(
            Status: result.Options.Count > 0 ? "options" : "no_fit",
            NoFit: result.NoFit?.ToString(),
            Draftable: result.Draftable.ToString(),
            PlacementValid: result.PlacementValid.ToString(),
            MaterialsReady: result.MaterialsReady.ToString(),
            ApplyReady: result.ApplyReady.ToString(),
            Trace: result.Trace,
            ErrorType: null,
            ErrorMessage: null);

    public static PlacementSolverReplayOutput FromFailure(string errorType, string errorMessage) =>
        new(
            Status: "error",
            NoFit: null,
            Draftable: null,
            PlacementValid: null,
            MaterialsReady: null,
            ApplyReady: null,
            Trace: null,
            ErrorType: errorType,
            ErrorMessage: errorMessage);
}
