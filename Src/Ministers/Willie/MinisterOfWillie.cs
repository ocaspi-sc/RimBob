using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.State;

namespace RimBob.Ministers.Willie;

public sealed class MinisterOfWillie(
    BriefingCache briefings,
    Rules rules,
    WillieSolveExecutor solveExecutor,
    IWillieSolveQueue solveQueue,
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
        IReadOnlyList<ZoneRequest> inboundZoneRequests = ActiveWillieZoneRequests(activeFlags, cycle.Flag);
        IReadOnlyList<WillieInboundRequest> inboundBoard = InboundBoardForRequests(
            AllActiveWillieBuildingRequests(activeFlags, cycle.Flag),
            activeFlags,
            cycle.Flag);
        IReadOnlyList<WillieInboundZoneRequest> inboundZoneBoard = InboundBoardForZoneRequests(
            AllActiveWillieZoneRequests(activeFlags, cycle.Flag),
            activeFlags,
            cycle.Flag);
        IReadOnlyList<WillieInboundRequest> solveBoard = InboundBoardForRequests(
            inboundRequests,
            activeFlags,
            cycle.Flag);
        IReadOnlyList<WillieInboundZoneRequest> zoneSolveBoard = InboundBoardForZoneRequests(
            inboundZoneRequests,
            activeFlags,
            cycle.Flag);
        solverStore.RecordInbound(Name, inboundBoard);
        solverStore.RecordZoneInbound(Name, inboundZoneBoard);

        RuleRun result = rules.Evaluate(briefing, inboundRequests, inboundZoneRequests);
        DecisionProjectionContext projectionContext = new(
            Minister: Name,
            Domain: "construction",
            BriefingVersion: briefing.BriefingVersion,
            GameDate: briefing.Date,
            GameTick: briefing.GameTick,
            Now: DateTimeOffset.UtcNow);
        PlacementSolverReplayOutput? placementReplayOutput = null;
        IReadOnlyList<AdviceItem> advice = DecisionProjection.ProjectAdvice(result.Decisions, projectionContext);
        IReadOnlyList<AgentFlag> emittedFlags = DecisionProjection.ProjectFlags(result.Decisions, projectionContext);
        Dictionary<string, PlacementSolveAttempt?> attemptsByRequestKey = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PlacementSolveAttempt?> zoneAttemptsByRequestKey = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RuleId> emittedRuleIds = EmittedAdviceRuleIds(result.Decisions);
        BuildingRequest? drivingBoardRequest = ContainsRule(emittedRuleIds, Rules.BuildingRequestActiveTrace)
            ? Rules.SelectPlacementRequest(briefing, inboundRequests)
            : null;
        ZoneRequest? drivingZoneRequest = ContainsRule(emittedRuleIds, Rules.ZoneRequestActiveTrace)
            ? Rules.SelectZoneRequest(inboundZoneRequests)
            : null;

        if (drivingBoardRequest is not null)
        {
            string drivingAdviceId = AdviceIdForRule(Rules.BuildingRequestActiveTrace);
            PlacementSolveAttempt? attempt = ResolveOrQueuePlacement(
                drivingBoardRequest,
                SourceMinisterForRequest(drivingBoardRequest, activeFlags, cycle.Flag),
                briefing,
                attemptsByRequestKey,
                drivingAdviceId,
                removeFallbackWhenApplyReady: false);
            if (attempt is null)
                advice = WillieAdviceComposer.MarkSolverPending(advice, drivingAdviceId);
            else
            {
                advice = WillieAdviceComposer.EnrichSolverAdvice(
                    advice,
                    attempt,
                    drivingAdviceId,
                    removeFallbackWhenApplyReady: false);
                placementReplayOutput = attempt.ReplayOutput;
            }
        }

        foreach (RuleId rule in emittedRuleIds.Where(IsMissingRoomRule))
        {
            if (!Rules.TryGetPlacementRequest(rule, briefing, inboundRequests, out BuildingRequest missingRoomRequest))
                continue;

            string drivingAdviceId = AdviceIdForRule(rule);
            PlacementSolveAttempt? attempt = ResolveOrQueuePlacement(
                missingRoomRequest,
                Name,
                briefing,
                attemptsByRequestKey,
                drivingAdviceId,
                removeFallbackWhenApplyReady: true);
            if (attempt is null)
                advice = WillieAdviceComposer.MarkSolverPending(advice, drivingAdviceId);
            else
            {
                advice = WillieAdviceComposer.EnrichSolverAdvice(
                    advice,
                    attempt,
                    drivingAdviceId,
                    removeFallbackWhenApplyReady: true);
                placementReplayOutput ??= attempt.ReplayOutput;
            }
        }

        foreach (WillieInboundRequest inbound in solveBoard)
        {
            if (drivingBoardRequest is not null &&
                string.Equals(inbound.RequestKey, WillieSolverStore.RequestKey(drivingBoardRequest), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PlacementSolveAttempt? attempt = ResolveOrQueuePlacement(
                inbound.Request,
                inbound.SourceMinister,
                briefing,
                attemptsByRequestKey,
                drivingAdviceId: null,
                removeFallbackWhenApplyReady: false);
            placementReplayOutput ??= attempt?.ReplayOutput;
        }

        if (drivingZoneRequest is not null)
        {
            string drivingAdviceId = AdviceIdForRule(Rules.ZoneRequestActiveTrace);
            PlacementSolveAttempt? attempt = ResolveOrQueueZonePlacement(
                drivingZoneRequest,
                SourceMinisterForRequest(drivingZoneRequest, activeFlags, cycle.Flag),
                briefing,
                zoneAttemptsByRequestKey,
                drivingAdviceId,
                removeFallbackWhenApplyReady: false);
            if (attempt is null)
                advice = WillieAdviceComposer.MarkSolverPending(advice, drivingAdviceId);
            else
            {
                advice = WillieAdviceComposer.EnrichSolverAdvice(
                    advice,
                    attempt,
                    drivingAdviceId,
                    removeFallbackWhenApplyReady: false);
                placementReplayOutput ??= attempt.ReplayOutput;
            }
        }

        foreach (WillieInboundZoneRequest inbound in zoneSolveBoard)
        {
            if (drivingZoneRequest is not null &&
                string.Equals(inbound.RequestKey, WillieSolverStore.RequestKey(drivingZoneRequest), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PlacementSolveAttempt? attempt = ResolveOrQueueZonePlacement(
                inbound.Request,
                inbound.SourceMinister,
                briefing,
                zoneAttemptsByRequestKey,
                drivingAdviceId: null,
                removeFallbackWhenApplyReady: false);
            placementReplayOutput ??= attempt?.ReplayOutput;
        }

        string stateSummary = WillieStateSummary.Build(briefing);
        string? trace = result.Decisions.Count > 0
            ? MinisterRuleTableEvaluator.CompositeTrace(result.Decisions)
            : result.Diagnostics.SelectedRule?.Value;
        PublishSnapshot(advice, emittedFlags, stateSummary);
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
            StateSummary: stateSummary,
            OutputKind: placementReplayOutput is null ? null : "placement_solver",
            Output: placementReplayOutput), ct);
        log.LogInformation(
            "Willie rules decision trace={Trace} advice={AdviceCount} flags={FlagCount}",
            trace, advice.Count, emittedFlags.Count);
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
        IReadOnlyList<BuildingRequest>? directRequests = directFlag?.BuildingRequests?
            .Where(IsRequestedFromWillie)
            .ToList();
        if (directRequests is { Count: > 0 })
            return directRequests;

        return AllActiveWillieBuildingRequests(activeFlags, directFlag);
    }

    private static IReadOnlyList<BuildingRequest> AllActiveWillieBuildingRequests(
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag) =>
        FlagsToRead(activeFlags, directFlag)
            .SelectMany(flag => flag.BuildingRequests ?? [])
            .Where(IsRequestedFromWillie)
            .ToList();

    private static bool IsRequestedFromWillie(BuildingRequest request) =>
        string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ZoneRequest> ActiveWillieZoneRequests(
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        IReadOnlyList<ZoneRequest>? directRequests = directFlag?.ZoneRequests?
            .Where(IsRequestedFromWillie)
            .ToList();
        if (directRequests is { Count: > 0 })
            return directRequests;

        return AllActiveWillieZoneRequests(activeFlags, directFlag);
    }

    private static IReadOnlyList<ZoneRequest> AllActiveWillieZoneRequests(
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag) =>
        FlagsToRead(activeFlags, directFlag)
            .SelectMany(flag => flag.ZoneRequests ?? [])
            .Where(IsRequestedFromWillie)
            .ToList();

    private static bool IsRequestedFromWillie(ZoneRequest request) =>
        string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<WillieInboundRequest> InboundBoardForRequests(
        IReadOnlyList<BuildingRequest> requests,
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag) =>
        requests
            .Select(request => new WillieInboundRequest(
                request,
                SourceMinisterForRequest(request, activeFlags, directFlag),
                WillieSolverStore.RequestKey(request)))
            .ToList();

    private static IReadOnlyList<WillieInboundZoneRequest> InboundBoardForZoneRequests(
        IReadOnlyList<ZoneRequest> requests,
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag) =>
        requests
            .Select(request => new WillieInboundZoneRequest(
                request,
                SourceMinisterForRequest(request, activeFlags, directFlag),
                WillieSolverStore.RequestKey(request)))
            .ToList();

    private static string? SourceMinisterForRequest(
        BuildingRequest request,
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        if (directFlag is not null &&
            (directFlag.BuildingRequests ?? []).Any(candidate =>
                IsRequestedFromWillie(candidate) && candidate == request))
        {
            return directFlag.SourceMinister;
        }

        return FlagsToRead(activeFlags, directFlag).FirstOrDefault(flag =>
                (flag.BuildingRequests ?? []).Any(candidate =>
                    IsRequestedFromWillie(candidate) && candidate == request))
            ?.SourceMinister;
    }

    private static string? SourceMinisterForRequest(
        ZoneRequest request,
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        if (directFlag is not null &&
            (directFlag.ZoneRequests ?? []).Any(candidate =>
                IsRequestedFromWillie(candidate) && candidate == request))
        {
            return directFlag.SourceMinister;
        }

        return FlagsToRead(activeFlags, directFlag).FirstOrDefault(flag =>
                (flag.ZoneRequests ?? []).Any(candidate =>
                    IsRequestedFromWillie(candidate) && candidate == request))
            ?.SourceMinister;
    }

    private static IReadOnlyList<AgentFlag> FlagsToRead(
        IReadOnlyList<AgentFlag> activeFlags,
        AgentFlag? directFlag)
    {
        List<AgentFlag> flagsToRead = [.. activeFlags];
        if (directFlag is not null &&
            flagsToRead.All(flag => !string.Equals(flag.Id, directFlag.Id, StringComparison.OrdinalIgnoreCase)))
        {
            flagsToRead.Add(directFlag);
        }

        return flagsToRead;
    }

    private PlacementSolveAttempt? ResolveOrQueuePlacement(
        BuildingRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        Dictionary<string, PlacementSolveAttempt?> attemptsByRequestKey,
        string? drivingAdviceId,
        bool removeFallbackWhenApplyReady)
    {
        string requestKey = WillieSolverStore.RequestKey(request);
        if (attemptsByRequestKey.TryGetValue(requestKey, out PlacementSolveAttempt? remembered))
            return remembered;

        IReadOnlyList<MaterialHint> materialsOnHand = WillieSolveExecutor.MaterialsOnHandFromStoredResources(colonyState);
        string inputFingerprint = solveExecutor.BuildingFingerprint(request, briefing, colonyState, materialsOnHand);
        PlacementSolveAttempt? cachedAttempt = solveExecutor.TryUseFreshBuildingOutcome(
            Name,
            request,
            sourceMinister,
            briefing,
            inputFingerprint);
        if (cachedAttempt is not null)
        {
            attemptsByRequestKey[requestKey] = cachedAttempt;
            return cachedAttempt;
        }

        WillieBuildingSolveJob job = new(
            JobId: Guid.NewGuid().ToString("N"),
            Minister: Name,
            RequestKey: requestKey,
            InputFingerprint: inputFingerprint,
            SourceMinister: sourceMinister,
            Briefing: briefing,
            FrozenState: ColonyStateFreeze.Capture(colonyState),
            DrivingAdviceId: drivingAdviceId,
            RemoveFallbackWhenApplyReady: removeFallbackWhenApplyReady,
            GameTick: briefing.GameTick,
            EnqueuedAt: DateTimeOffset.UtcNow,
            Request: request,
            MaterialsOnHand: materialsOnHand);
        WillieSolveEnqueueResult enqueue = solveQueue.TryEnqueue(job);
        PlacementSolveAttempt? attempt = enqueue.InlineAttempt ?? RejectedAttempt(enqueue, zone: false);
        attemptsByRequestKey[requestKey] = attempt;
        return attempt;
    }

    private PlacementSolveAttempt? ResolveOrQueueZonePlacement(
        ZoneRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        Dictionary<string, PlacementSolveAttempt?> attemptsByRequestKey,
        string? drivingAdviceId,
        bool removeFallbackWhenApplyReady)
    {
        string requestKey = WillieSolverStore.RequestKey(request);
        if (attemptsByRequestKey.TryGetValue(requestKey, out PlacementSolveAttempt? remembered))
            return remembered;

        string inputFingerprint = solveExecutor.ZoneFingerprint(request, briefing, colonyState);
        PlacementSolveAttempt? cachedAttempt = solveExecutor.TryUseFreshZoneOutcome(
            Name,
            request,
            sourceMinister,
            briefing,
            inputFingerprint);
        if (cachedAttempt is not null)
        {
            attemptsByRequestKey[requestKey] = cachedAttempt;
            return cachedAttempt;
        }

        WillieZoneSolveJob job = new(
            JobId: Guid.NewGuid().ToString("N"),
            Minister: Name,
            RequestKey: requestKey,
            InputFingerprint: inputFingerprint,
            SourceMinister: sourceMinister,
            Briefing: briefing,
            FrozenState: ColonyStateFreeze.Capture(colonyState),
            DrivingAdviceId: drivingAdviceId,
            RemoveFallbackWhenApplyReady: removeFallbackWhenApplyReady,
            GameTick: briefing.GameTick,
            EnqueuedAt: DateTimeOffset.UtcNow,
            Request: request);
        WillieSolveEnqueueResult enqueue = solveQueue.TryEnqueue(job);
        PlacementSolveAttempt? attempt = enqueue.InlineAttempt ?? RejectedAttempt(enqueue, zone: true);
        attemptsByRequestKey[requestKey] = attempt;
        return attempt;
    }

    private static PlacementSolveAttempt? RejectedAttempt(WillieSolveEnqueueResult enqueue, bool zone)
    {
        if (enqueue.State != WillieSolveQueueItemState.Rejected)
            return null;

        InvalidOperationException ex = new("Willie solve queue is full.");
        return zone
            ? PlacementSolveAttempt.FromZoneFailure(ex)
            : PlacementSolveAttempt.FromFailure(ex);
    }

    private static IReadOnlyList<RuleId> EmittedAdviceRuleIds(IReadOnlyList<Decision> decisions) =>
        decisions
            .OfType<Advise>()
            .Select(advice => advice.Rule)
            .Distinct()
            .ToList();

    private static bool ContainsRule(IReadOnlyList<RuleId> rules, RuleId rule) =>
        rules.Contains(rule);

    private static string AdviceIdForRule(RuleId rule) =>
        $"willie_{rule.Value}";

    private static bool IsMissingRoomRule(RuleId rule) =>
        rule.Value is "kitchen_missing" or "hospital_missing" or "storage_room_missing";

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

}
