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
    IPlacementSolver placementSolver,
    IGrowZonePlacementSolver growZonePlacementSolver,
    ColonyState colonyState,
    WillieSolverStore solverStore,
    MinisterOutputStore outputStore,
    AdviceBus bus,
    FlagChannel flags,
    MinisterTraceStore traces,
    ILogger<MinisterOfWillie> log,
    MinisterReplayRecorder? replay = null) : IMinister
{
    private const string SolverOfflineTraceNote = "solver offline; preserved prior advice";

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
        Dictionary<string, PlacementSolveAttempt> attemptsByRequestKey = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PlacementSolveAttempt> zoneAttemptsByRequestKey = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RuleId> emittedRuleIds = EmittedAdviceRuleIds(result.Decisions);
        BuildingRequest? drivingBoardRequest = ContainsRule(emittedRuleIds, Rules.BuildingRequestActiveTrace)
            ? Rules.SelectPlacementRequest(briefing, inboundRequests)
            : null;
        ZoneRequest? drivingZoneRequest = ContainsRule(emittedRuleIds, Rules.ZoneRequestActiveTrace)
            ? Rules.SelectZoneRequest(inboundZoneRequests)
            : null;

        if (drivingBoardRequest is not null)
        {
            PlacementSolveAttempt attempt = await SolveAndRecordPlacementAsync(
                drivingBoardRequest,
                SourceMinisterForRequest(drivingBoardRequest, activeFlags, cycle.Flag),
                briefing,
                attemptsByRequestKey,
                ct);
            advice = EnrichSolverAdvice(
                advice,
                attempt,
                AdviceIdForRule(Rules.BuildingRequestActiveTrace),
                removeFallbackWhenApplyReady: false);
            placementReplayOutput = attempt.ReplayOutput;

            if (attempt.SolverOffline)
            {
                await PreserveSolverOfflineReplayAsync(result, cycle, briefing, context, advice, emittedFlags, attempt.ReplayOutput, ct);
                return;
            }
        }

        foreach (RuleId rule in emittedRuleIds.Where(IsMissingRoomRule))
        {
            if (!Rules.TryGetPlacementRequest(rule, briefing, inboundRequests, out BuildingRequest missingRoomRequest))
                continue;

            PlacementSolveAttempt attempt = await SolveAndRecordPlacementAsync(
                missingRoomRequest,
                Name,
                briefing,
                attemptsByRequestKey,
                ct);
            advice = EnrichSolverAdvice(
                advice,
                attempt,
                AdviceIdForRule(rule),
                removeFallbackWhenApplyReady: true);
            placementReplayOutput ??= attempt.ReplayOutput;

            if (attempt.SolverOffline)
            {
                await PreserveSolverOfflineReplayAsync(result, cycle, briefing, context, advice, emittedFlags, attempt.ReplayOutput, ct);
                return;
            }
        }

        foreach (WillieInboundRequest inbound in solveBoard)
        {
            if (drivingBoardRequest is not null &&
                string.Equals(inbound.RequestKey, WillieSolverStore.RequestKey(drivingBoardRequest), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PlacementSolveAttempt attempt = await SolveAndRecordPlacementAsync(
                inbound.Request,
                inbound.SourceMinister,
                briefing,
                attemptsByRequestKey,
                ct);
            if (attempt.SolverOffline)
            {
                await PreserveSolverOfflineReplayAsync(result, cycle, briefing, context, advice, emittedFlags, attempt.ReplayOutput, ct);
                return;
            }
        }

        if (drivingZoneRequest is not null)
        {
            PlacementSolveAttempt attempt = await SolveAndRecordZonePlacementAsync(
                drivingZoneRequest,
                SourceMinisterForRequest(drivingZoneRequest, activeFlags, cycle.Flag),
                briefing,
                zoneAttemptsByRequestKey,
                ct);
            advice = EnrichSolverAdvice(
                advice,
                attempt,
                AdviceIdForRule(Rules.ZoneRequestActiveTrace),
                removeFallbackWhenApplyReady: false);
            placementReplayOutput ??= attempt.ReplayOutput;
        }

        foreach (WillieInboundZoneRequest inbound in zoneSolveBoard)
        {
            if (drivingZoneRequest is not null &&
                string.Equals(inbound.RequestKey, WillieSolverStore.RequestKey(drivingZoneRequest), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            PlacementSolveAttempt attempt = await SolveAndRecordZonePlacementAsync(
                inbound.Request,
                inbound.SourceMinister,
                briefing,
                zoneAttemptsByRequestKey,
                ct);
            placementReplayOutput ??= attempt.ReplayOutput;
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

    private async Task<PlacementSolveAttempt> SolveAndRecordPlacementAsync(
        BuildingRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        Dictionary<string, PlacementSolveAttempt> attemptsByRequestKey,
        CancellationToken ct)
    {
        string requestKey = WillieSolverStore.RequestKey(request);
        if (attemptsByRequestKey.TryGetValue(requestKey, out PlacementSolveAttempt? cached))
            return cached;

        PlacementSolveAttempt attempt = await TrySolvePlacementAsync(request, briefing, ct);
        attemptsByRequestKey[requestKey] = attempt;
        solverStore.Record(new WillieSolverSnapshot(
            Minister: Name,
            Request: WillieSolverRequestSnapshot.FromRequest(request, sourceMinister),
            GameTick: briefing.GameTick,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: attempt.ReplayOutput,
            Options: attempt.Result?.Options ?? []));
        return attempt;
    }

    private async Task<PlacementSolveAttempt> SolveAndRecordZonePlacementAsync(
        ZoneRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        Dictionary<string, PlacementSolveAttempt> attemptsByRequestKey,
        CancellationToken ct)
    {
        string requestKey = WillieSolverStore.RequestKey(request);
        if (attemptsByRequestKey.TryGetValue(requestKey, out PlacementSolveAttempt? cached))
            return cached;

        PlacementSolveAttempt attempt = await TrySolveZonePlacementAsync(request, briefing, ct);
        attemptsByRequestKey[requestKey] = attempt;
        solverStore.RecordZoneOutcome(new WillieZoneSolverSnapshot(
            Minister: Name,
            Request: WillieZoneRequestSnapshot.FromRequest(request, sourceMinister),
            GameTick: briefing.GameTick,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: attempt.ReplayOutput,
            Options: attempt.Result?.Options ?? []));
        return attempt;
    }

    private async Task PreserveSolverOfflineReplayAsync(
        RuleRun ruleRun,
        PlayCycleContext cycle,
        WillieBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<AdviceItem> advice,
        IReadOnlyList<AgentFlag> emittedFlags,
        PlacementSolverReplayOutput replayOutput,
        CancellationToken ct)
    {
        string preservedStateSummary = WillieStateSummary.Build(briefing);
        string? trace = ruleRun.Decisions.Count > 0
            ? MinisterRuleTableEvaluator.CompositeTrace(ruleRun.Decisions)
            : ruleRun.Diagnostics.SelectedRule?.Value;
        await PersistReplayAsync(new MinisterReplayEntry(
            Minister: Name,
            Cycle: cycle,
            Path: "rules",
            Briefing: briefing,
            Context: context,
            RuleTrace: trace,
            RuleDiagnostics: ruleRun.Diagnostics,
            Advice: advice,
            Flags: emittedFlags,
            StateSummary: preservedStateSummary,
            OutputKind: "placement_solver",
            Output: replayOutput), ct);
        traces.Complete(Name, SolverOfflineTraceNote);
        log.LogInformation(
            "Willie placement solver offline for trace={Trace}; preserved prior advice snapshot",
            trace);
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
        catch (Exception ex) when (
            RimApiConnectionFailure.IsConnectionFailure(ex) ||
            ex is RimApiLiveStateUnavailableException)
        {
            log.LogWarning(
                ex,
                "Willie placement solver is offline for request={Request}; preserving prior advice.",
                request.Request);
            return PlacementSolveAttempt.FromSolverOffline(ex);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Willie placement solver failed for request={Request}", request.Request);
            return PlacementSolveAttempt.FromFailure(ex);
        }
    }

    private async Task<PlacementSolveAttempt> TrySolveZonePlacementAsync(
        ZoneRequest request,
        WillieBriefing briefing,
        CancellationToken ct)
    {
        try
        {
            PlacementResult result = await growZonePlacementSolver.SolveAsync(request, briefing, colonyState, ct);
            return PlacementSolveAttempt.FromZoneResult(result, request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Willie grow-zone placement solver failed for request={Request}", request.Request);
            return PlacementSolveAttempt.FromZoneFailure(ex);
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

    private static IReadOnlyList<RuleId> EmittedAdviceRuleIds(IReadOnlyList<Decision> decisions) =>
        decisions
            .OfType<Advise>()
            .Select(advice => advice.Rule)
            .Distinct()
            .ToList();

    private static bool ContainsRule(IReadOnlyList<RuleId> rules, RuleId rule) =>
        rules.Contains(rule);

    private static AdviceItem EnrichAdviceItem(
        AdviceItem item,
        PlacementSolveAttempt attempt,
        bool removeFallbackWhenApplyReady)
    {
        if (attempt.Result is null)
            return item with
            {
                Body = AppendPlacementBodyNote(item.Body, attempt.AdviceBodyNote),
                Rationale = AppendPlacementNote(item.Rationale, attempt.Note)
            };

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
            ? baseActions.Concat(options!.Select(ApplyActionForOption).Where(action => action is not null).Cast<AdviceAction>()).ToList()
            : baseActions;

        return item with
        {
            Body = AppendPlacementBodyNote(item.Body, attempt.AdviceBodyNote),
            Options = options,
            Actions = actions,
            Rationale = AppendPlacementNote(item.Rationale, attempt.Note)
        };
    }

    private static AdviceAction? ApplyActionForOption(AdviceOption option)
    {
        if (IsZoneCellOption(option))
            return GrowingZoneApplyActionForOption(option);

        return new AdviceAction(
            AdviceActionKind.PlaceBlueprint,
            $"Place the {option.Label} blueprint group.",
            Owner: "Willie",
            Apply: new PlaceBlueprintGroupApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                BlueprintGroup: option.BlueprintGroup,
                AssetCount: option.BlueprintGroup.Assets.Count));
    }

    private static AdviceAction? GrowingZoneApplyActionForOption(AdviceOption option)
    {
        IReadOnlyList<BlueprintAsset> zoneAssets = option.BlueprintGroup.Assets
            .Where(asset => string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (zoneAssets.Count == 0 || zoneAssets.Count != option.BlueprintGroup.Assets.Count)
            return null;

        IReadOnlyList<string> plantDefs = zoneAssets
            .Select(asset => asset.DefName)
            .Where(def => !string.IsNullOrWhiteSpace(def))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plantDefs.Count != 1)
            return null;

        IReadOnlyList<MapCell> cells = zoneAssets
            .Select(asset => asset.Cell)
            .Distinct()
            .ToList();
        if (cells.Count != zoneAssets.Count)
            return null;

        MapRect rect = new(
            X1: cells.Min(cell => cell.X),
            Z1: cells.Min(cell => cell.Z),
            X2: cells.Max(cell => cell.X),
            Z2: cells.Max(cell => cell.Z));
        if (rect.Area != cells.Count)
            return null;

        return new AdviceAction(
            AdviceActionKind.DesignateZone,
            $"Create the {option.Label} growing zone.",
            Quantity: cells.Count,
            Owner: "Willie",
            WorkType: WorkType.Grow,
            Apply: new CreateGrowingZoneApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                PlantDef: plantDefs[0],
                Rect: rect,
                TargetCount: cells.Count));
    }

    private static bool IsZoneCellOption(AdviceOption option) =>
        option.BlueprintGroup.Assets.Any(asset =>
            string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase));

    private static string AppendPlacementNote(string text, string note) =>
        string.IsNullOrWhiteSpace(text) ? note : $"{text} {note}";

    private static string AppendPlacementBodyNote(string body, string? note) =>
        string.IsNullOrWhiteSpace(note) ? body : AppendPlacementNote(body, note);

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

    private static string NoteFor(PlacementResult result, ZoneRequest request)
    {
        if (result.Options.Count > 0)
        {
            return $"Grow-zone solver: {result.Options.Count} inspected option{Plural(result.Options.Count)}; apply_ready={ReadinessWire(result.ApplyReady)} via create_growing_zone validation.";
        }

        string reason = result.NoFit is null
            ? "no validated growing-zone option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Grow-zone solver no-fit: {reason}.";
    }

    private static string? AdviceBodyNoteFor(PlacementResult result, BuildingRequest request)
    {
        if (result.Options.Count > 0) return null;

        string reason = result.NoFit is null
            ? $"no validated {RequestRoomLabel(request)} option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Placement solver could not suggest layout options because {reason}.";
    }

    private static string? AdviceBodyNoteFor(PlacementResult result, ZoneRequest request)
    {
        if (result.Options.Count > 0)
            return $"Grow-zone solver found {result.Options.Count} coordinate option{Plural(result.Options.Count)} below; Apply validates live terrain and occupancy before creating the zone.";

        string reason = result.NoFit is null
            ? "no validated growing-zone option was emitted"
            : NoFitNote(result.NoFit.Value, request);
        return $"Grow-zone solver could not suggest zone options because {reason}.";
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

    private static string NoFitNote(NoFitReason reason, ZoneRequest request) => reason switch
    {
        NoFitReason.UnsupportedZoneClass => $"{request.ZoneClass} is not supported by the grow-zone solver",
        NoFitReason.NoTerrainGrid => "cell-level terrain is unavailable in the current state snapshot",
        NoFitReason.NoGrowableCells => "no growable terrain cells were found inside the Home/buildable bounds",
        NoFitReason.AllZoneCellsBlocked => "all growable cells were blocked by existing zones or occupancy",
        NoFitReason.NoZoneRectangle => "no compact unoccupied growable rectangle matched the requested tile count",
        _ => "no validated growing-zone option was emitted"
    };

    private static string AdviceIdForRule(RuleId rule) =>
        $"willie_{rule.Value}";

    private static bool IsMissingRoomRule(RuleId rule) =>
        rule.Value is "kitchen_missing" or "hospital_missing" or "storage_room_missing";

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
        string? AdviceBodyNote,
        PlacementSolverReplayOutput ReplayOutput,
        bool SolverOffline)
    {
        public static PlacementSolveAttempt FromResult(PlacementResult result, BuildingRequest request) =>
            new(
                result,
                NoteFor(result, request),
                AdviceBodyNoteFor(result, request),
                PlacementSolverReplayOutput.FromResult(result),
                SolverOffline: false);

        public static PlacementSolveAttempt FromZoneResult(PlacementResult result, ZoneRequest request) =>
            new(
                result,
                NoteFor(result, request),
                AdviceBodyNoteFor(result, request),
                PlacementSolverReplayOutput.FromResult(result),
                SolverOffline: false);

        public static PlacementSolveAttempt FromFailure(Exception ex)
        {
            string errorType = ex.GetType().Name;
            string note = $"Placement solver unavailable: {errorType}. Keeping prose advice.";
            string bodyNote = $"Placement solver could not suggest layout options because it hit {errorType} before validation completed.";
            return new PlacementSolveAttempt(
                null,
                note,
                bodyNote,
                PlacementSolverReplayOutput.FromFailure(errorType, ex.Message),
                SolverOffline: false);
        }

        public static PlacementSolveAttempt FromSolverOffline(Exception ex)
        {
            string errorType = ex.GetType().Name;
            string note = $"Placement solver offline: {errorType}. Preserved prior advice.";
            string bodyNote = $"Placement solver could not refresh layout options because live map validation was unavailable ({errorType}); preserved prior advice.";
            return new PlacementSolveAttempt(
                null,
                note,
                bodyNote,
                PlacementSolverReplayOutput.FromOffline(errorType, ex.Message),
                SolverOffline: true);
        }

        public static PlacementSolveAttempt FromZoneFailure(Exception ex)
        {
            string errorType = ex.GetType().Name;
            string note = $"Grow-zone solver unavailable: {errorType}. Keeping prose advice.";
            string bodyNote = $"Grow-zone solver could not suggest zone options because it hit {errorType} before scoring completed.";
            return new PlacementSolveAttempt(
                null,
                note,
                bodyNote,
                PlacementSolverReplayOutput.FromFailure(errorType, ex.Message),
                SolverOffline: false);
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

    public static PlacementSolverReplayOutput FromOffline(string errorType, string errorMessage) =>
        new(
            Status: "offline",
            NoFit: null,
            Draftable: null,
            PlacementValid: null,
            MaterialsReady: null,
            ApplyReady: null,
            Trace: null,
            ErrorType: errorType,
            ErrorMessage: errorMessage);
}
