using Microsoft.Extensions.Logging;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;
using RimBob.State;
using System.Text;

namespace RimBob.Coordination;

public sealed class CabinetCycle(
    IColonyStateRefresher ingestion,
    ColonyState colony,
    ColonyStateSnapshotStore snapshotStore,
    IEnumerable<IMinister> ministers,
    MinisterRegistry registry,
    MinisterTraceStore traces,
    FlagChannel flags,
    CabinetRunLogStore runLogs,
    ILogger<CabinetCycle> log)
{
    private const string WillieRunStepKey = "minister_willie";

    public async Task RunAsync(CancellationToken ct) =>
        await RunCycleAsync(PlayCycleContext.ManualTrigger, ct);

    public async Task RunAsync(PlayCycleContext cycle, CancellationToken ct) =>
        await RunCycleAsync(cycle, ct);

    public async Task<CabinetTriggerResult> TriggerCabinetAsync(CancellationToken ct, string? runId = null)
    {
        CabinetRunLogSnapshot startedRun = runLogs.StartRun(
            runId,
            "cabinet",
            PlayCycleContext.ManualTrigger.Trigger.ToString());
        bool? usedRestoredSnapshot = null;

        try
        {
            usedRestoredSnapshot = await RunCycleAsync(PlayCycleContext.ManualTrigger, ct, startedRun.RunId);
            CabinetRunLogSnapshot completedRun = runLogs.CompleteRun(
                startedRun.RunId,
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot.Value);
            return new CabinetTriggerResult(
                "cabinet",
                PlayCycleContext.ManualTrigger.Trigger.ToString(),
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot.Value,
                completedRun);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            runLogs.FailRun(
                startedRun.RunId,
                ex,
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot);
            throw;
        }
    }

    public async Task<CabinetTriggerResult> TriggerCabinetRulesOnlyAsync(CancellationToken ct, string? runId = null)
    {
        CabinetRunLogSnapshot startedRun = runLogs.StartRun(
            runId,
            "cabinet_rules",
            PlayCycleContext.ManualCabinetRulesOnly.Trigger.ToString());
        bool? usedRestoredSnapshot = null;

        try
        {
            usedRestoredSnapshot = await RunCycleAsync(
                PlayCycleContext.ManualCabinetRulesOnly,
                ct,
                startedRun.RunId,
                rulesOnlyCabinet: true);
            CabinetRunLogSnapshot completedRun = runLogs.CompleteRun(
                startedRun.RunId,
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot.Value);
            return new CabinetTriggerResult(
                "cabinet_rules",
                PlayCycleContext.ManualCabinetRulesOnly.Trigger.ToString(),
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot.Value,
                completedRun);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            runLogs.FailRun(
                startedRun.RunId,
                ex,
                colony.LastRefreshSource.ToString(),
                usedRestoredSnapshot);
            throw;
        }
    }

    private async Task<bool> RunCycleAsync(
        PlayCycleContext cycle,
        CancellationToken ct,
        string? cabinetRunId = null,
        bool rulesOnlyCabinet = false)
    {
        bool usedRestoredSnapshot = await RefreshStateForReadOnlyEvaluationAsync(
            cycle,
            "cabinet",
            ct,
            cabinetRunId);
        HashSet<string> ministersRunAsRequestFollowUp = new(StringComparer.OrdinalIgnoreCase);

        foreach (MinisterDescriptor descriptor in registry.CabinetMinisters)
        {
            if (ministersRunAsRequestFollowUp.Contains(descriptor.Key))
            {
                log.LogInformation(
                    "Cabinet cycle: skipping scheduled {Minister}; already ran for a newly published build request.",
                    descriptor.Label);
                continue;
            }

            if (rulesOnlyCabinet && !descriptor.CanRunRules)
            {
                if (cabinetRunId is not null)
                {
                    runLogs.SkipStep(
                        cabinetRunId,
                        $"minister_{descriptor.Key}",
                        $"{descriptor.Label} run",
                        "minister",
                        "Skipped - LLM-only minister excluded from a rules-only cabinet run.",
                        descriptor.Label);
                }

                log.LogInformation(
                    "Cabinet rules-only cycle: skipping {Minister}; it has no rules-only path.",
                    descriptor.Label);
                continue;
            }

            IMinister? minister = ResolveMinister(descriptor);
            if (minister is null)
                throw new InvalidOperationException($"Cabinet cycle could not resolve {descriptor.Label} minister.");

            log.LogInformation("Cabinet cycle: running {Minister}", descriptor.Label);
            long flagSequenceBeforeRun = flags.CurrentSequence;
            await RunResolvedMinisterAsync(minister, descriptor, cycle, usedRestoredSnapshot, ct, cabinetRunId);
            IReadOnlyList<string> followUpKeys = await RunWillieRequestFollowUpsAsync(
                descriptor,
                flagSequenceBeforeRun,
                usedRestoredSnapshot,
                ct,
                cabinetRunId);
            foreach (string followUpKey in followUpKeys)
                ministersRunAsRequestFollowUp.Add(followUpKey);
        }

        return usedRestoredSnapshot;
    }

    public async Task<MinisterTriggerResult?> TriggerMinisterAsync(
        string ministerKey,
        MinisterRunMode runMode,
        CancellationToken ct)
    {
        MinisterDescriptor? descriptor = registry.FindMinister(ministerKey);
        if (descriptor is not { Ready: true }) return null;
        if (!CanTriggerMode(descriptor, runMode)) return null;

        PlayCycleContext cycle = ManualCycleFor(runMode);
        bool usedRestoredSnapshot = await RefreshStateForReadOnlyEvaluationAsync(
            cycle,
            descriptor.Label,
            ct,
            cabinetRunId: null);

        IMinister? minister = ResolveMinister(descriptor);
        if (minister is null)
            throw new InvalidOperationException($"Manual trigger could not resolve {descriptor.Label} minister.");

        long flagSequenceBeforeRun = flags.CurrentSequence;
        await RunResolvedMinisterAsync(minister, descriptor, cycle, usedRestoredSnapshot, ct);
        await RunWillieRequestFollowUpsAsync(
            descriptor,
            flagSequenceBeforeRun,
            usedRestoredSnapshot,
            ct);
        return new MinisterTriggerResult(
            descriptor.Key,
            descriptor.Label,
            cycle.Trigger.ToString(),
            runMode.ToString(),
            colony.LastRefreshSource.ToString(),
            usedRestoredSnapshot);
    }

    public async Task<MinisterTriggerResult?> TriggerMinisterAsync(string ministerKey, CancellationToken ct) =>
        await TriggerMinisterAsync(ministerKey, MinisterRunMode.RulesFirst, ct);

    private async Task<bool> RefreshStateForReadOnlyEvaluationAsync(
        PlayCycleContext cycle,
        string scope,
        CancellationToken ct,
        string? cabinetRunId)
    {
        if (cabinetRunId is not null)
        {
            runLogs.StartStep(
                cabinetRunId,
                "live_state_refresh",
                "Live-state refresh",
                "refresh",
                "Refreshing live colony state before read-only evaluation.");
        }

        try
        {
            await ingestion.RefreshAllAsync(ct);
            if (cabinetRunId is not null)
            {
                runLogs.CompleteStep(
                    cabinetRunId,
                    "live_state_refresh",
                    "Live-state refresh",
                    "refresh",
                    "Live colony state refreshed.",
                    stateSource: colony.LastRefreshSource.ToString(),
                    usedRestoredSnapshot: false);
            }
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            if (!TryRestoreSnapshotForManualFallback(cycle, ex))
            {
                if (cabinetRunId is not null)
                {
                    runLogs.FailStep(
                        cabinetRunId,
                        "live_state_refresh",
                        "Live-state refresh",
                        "refresh",
                        ex,
                        "Live state refresh failed and no restored snapshot fallback was eligible.",
                        stateSource: colony.LastRefreshSource.ToString(),
                        usedRestoredSnapshot: false);
                }
                throw;
            }

            if (cabinetRunId is not null)
            {
                runLogs.FailStep(
                    cabinetRunId,
                    "live_state_refresh",
                    "Live-state refresh",
                    "refresh",
                    ex,
                    "Live state refresh failed; using restored snapshot for read-only evaluation.",
                    stateSource: colony.LastRefreshSource.ToString(),
                    usedRestoredSnapshot: true);
                runLogs.StartStep(
                    cabinetRunId,
                    "restored_snapshot_fallback",
                    "Restored-snapshot fallback",
                    "fallback",
                    "Restoring last curated ColonyState snapshot.");
                runLogs.CompleteStep(
                    cabinetRunId,
                    "restored_snapshot_fallback",
                    "Restored-snapshot fallback",
                    "fallback",
                    "Restored snapshot state is active for this read-only run.",
                    stateSource: colony.LastRefreshSource.ToString(),
                    usedRestoredSnapshot: true);
            }

            log.LogWarning(
                ex,
                "Live state refresh failed for manual {Scope} trigger; restored colony snapshot state for read-only evaluation.",
                scope);
            return true;
        }
    }

    private bool TryRestoreSnapshotForManualFallback(PlayCycleContext cycle, Exception ex)
    {
        if (cycle.Trigger != PlayCycleTrigger.ManualTrigger) return false;
        if (snapshotStore.Latest is null) return false;
        if (!CanUseSnapshotFallback(ex)) return false;

        snapshotStore.RestoreInto(colony);
        return true;
    }

    private static bool CanUseSnapshotFallback(Exception ex) =>
        RimApiConnectionFailure.IsConnectionFailure(ex) ||
        ex is RimApiLiveStateUnavailableException;

    private static bool CanTriggerMode(MinisterDescriptor descriptor, MinisterRunMode runMode) =>
        runMode switch
        {
            MinisterRunMode.RulesOnly => descriptor.CanRunRules,
            MinisterRunMode.ForceLlm => descriptor.CanRunLlm,
            _ => descriptor.CanManualTrigger
        };

    private static PlayCycleContext ManualCycleFor(MinisterRunMode runMode) =>
        runMode switch
        {
            MinisterRunMode.RulesOnly => PlayCycleContext.ManualRulesOnly,
            MinisterRunMode.ForceLlm => PlayCycleContext.ManualForceLlm,
            _ => PlayCycleContext.ManualTrigger
        };

    private async Task RunResolvedMinisterAsync(
        IMinister minister,
        MinisterDescriptor descriptor,
        PlayCycleContext cycle,
        bool usedRestoredSnapshot,
        CancellationToken ct,
        string? cabinetRunId = null)
    {
        string ministerStepKey = $"minister_{descriptor.Key}";
        if (cabinetRunId is not null)
        {
            runLogs.StartStep(
                cabinetRunId,
                ministerStepKey,
                $"{descriptor.Label} run",
                "minister",
                $"Running {descriptor.Label}.",
                descriptor.Label);
        }

        traces.Begin(minister.Name, cycle);
        try
        {
            await minister.RunPlayCycle(cycle, ct);
            traces.Complete(
                minister.Name,
                usedRestoredSnapshot
                    ? "Live refresh failed; evaluated against restored colony snapshot state."
                    : null);
            if (cabinetRunId is not null)
            {
                MinisterTraceSnapshot? trace = LatestTraceFor(descriptor, minister);
                runLogs.CompleteStep(
                    cabinetRunId,
                    ministerStepKey,
                    $"{descriptor.Label} run",
                    "minister",
                    MinisterStepDetail(descriptor.Label, trace),
                    descriptor.Label,
                    colony.LastRefreshSource.ToString(),
                    usedRestoredSnapshot,
                    trace);
            }
        }
        catch (Exception ex)
        {
            traces.Fail(minister.Name, ex);
            if (cabinetRunId is not null)
            {
                MinisterTraceSnapshot? trace = LatestTraceFor(descriptor, minister);
                runLogs.FailStep(
                    cabinetRunId,
                    ministerStepKey,
                    $"{descriptor.Label} run",
                    "minister",
                    ex,
                    MinisterStepDetail(descriptor.Label, trace),
                    descriptor.Label,
                    colony.LastRefreshSource.ToString(),
                    usedRestoredSnapshot,
                    trace);
            }
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> RunWillieRequestFollowUpsAsync(
        MinisterDescriptor sourceDescriptor,
        long flagSequenceBeforeRun,
        bool usedRestoredSnapshot,
        CancellationToken ct,
        string? cabinetRunId = null)
    {
        List<AgentFlag> requestFlags = flags.PublishedAfter(flagSequenceBeforeRun)
            .Select(entry => entry.Flag)
            .Where(flag => IsFlagFromDescriptor(flag, sourceDescriptor))
            .Where(HasWillieRequest)
            .GroupBy(flag => flag.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        if (requestFlags.Count == 0) return [];

        MinisterDescriptor? willie = registry.FindMinister("willie");
        if (willie is not { Ready: true, CanRunRules: true }) return [];
        if (sourceDescriptor.Key.Equals(willie.Key, StringComparison.OrdinalIgnoreCase)) return [];

        IMinister? minister = ResolveMinister(willie);
        if (minister is null)
            throw new InvalidOperationException("Willie build-request follow-up could not resolve Willie minister.");

        if (cabinetRunId is not null)
        {
            runLogs.StartOrResumeStep(
                cabinetRunId,
                WillieRunStepKey,
                $"{willie.Label} run",
                "minister",
                $"Running {willie.Label} request follow-up solves.",
                willie.Label);
        }

        foreach (AgentFlag requestFlag in requestFlags)
        {
            log.LogInformation(
                "Cabinet cycle: running Willie for routed request flag {FlagId} from {SourceMinister}",
                requestFlag.Id,
                requestFlag.SourceMinister);
            string wakeupPayload = WillieWakeupPayload(requestFlag);
            PlayCycleContext requestCycle = new(
                PlayCycleTrigger.FlagFired,
                Flag: requestFlag,
                WakeupPayload: wakeupPayload,
                RunMode: MinisterRunMode.RulesOnly);
            DateTimeOffset childStartedAt = DateTimeOffset.UtcNow;
            try
            {
                await RunResolvedMinisterAsync(
                    minister,
                    willie,
                    requestCycle,
                    usedRestoredSnapshot,
                    ct,
                    cabinetRunId: null);

                if (cabinetRunId is not null)
                {
                    DateTimeOffset childCompletedAt = DateTimeOffset.UtcNow;
                    MinisterTraceSnapshot? trace = LatestTraceFor(willie, minister);
                    runLogs.AddChildStep(
                        cabinetRunId,
                        WillieRunStepKey,
                        WillieChildStep(
                            requestFlag,
                            willie,
                            childStartedAt,
                            childCompletedAt,
                            "completed",
                            usedRestoredSnapshot,
                            colony.LastRefreshSource.ToString(),
                            trace,
                            error: null));
                }
            }
            catch (Exception ex)
            {
                if (cabinetRunId is not null)
                {
                    DateTimeOffset childCompletedAt = DateTimeOffset.UtcNow;
                    MinisterTraceSnapshot? trace = LatestTraceFor(willie, minister);
                    runLogs.AddChildStep(
                        cabinetRunId,
                        WillieRunStepKey,
                        WillieChildStep(
                            requestFlag,
                            willie,
                            childStartedAt,
                            childCompletedAt,
                            "failed",
                            usedRestoredSnapshot,
                            colony.LastRefreshSource.ToString(),
                            trace,
                            ex));
                }

                throw;
            }
        }

        return [willie.Key];
    }

    private static bool IsFlagFromDescriptor(AgentFlag flag, MinisterDescriptor descriptor)
    {
        string source = MinisterRegistry.NormalizeKey(flag.SourceMinister);
        return source.Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase) ||
               source.Equals(MinisterRegistry.NormalizeKey(descriptor.Label), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasWillieRequest(AgentFlag flag) =>
        HasWillieBuildingRequest(flag) ||
        HasWillieZoneRequest(flag);

    private static bool HasWillieBuildingRequest(AgentFlag flag) =>
        (flag.BuildingRequests ?? [])
        .Any(request => string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase));

    private static bool HasWillieZoneRequest(AgentFlag flag) =>
        (flag.ZoneRequests ?? [])
        .Any(request => string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase));

    private static string WillieWakeupPayload(AgentFlag flag)
    {
        bool hasBuildingRequest = HasWillieBuildingRequest(flag);
        bool hasZoneRequest = HasWillieZoneRequest(flag);
        if (hasBuildingRequest && hasZoneRequest) return $"willie_request:{flag.Id}";
        return hasZoneRequest ? $"zone_request:{flag.Id}" : $"building_request:{flag.Id}";
    }

    private static CabinetRunStepSnapshot WillieChildStep(
        AgentFlag requestFlag,
        MinisterDescriptor willie,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        bool usedRestoredSnapshot,
        string stateSource,
        MinisterTraceSnapshot? trace,
        Exception? error)
    {
        string detail = MinisterStepDetail(willie.Label, trace);
        return new CabinetRunStepSnapshot(
            Key: WillieChildStepKey(requestFlag),
            Label: WillieChildLabel(requestFlag),
            Kind: "solver",
            Status: status,
            StartedAt: startedAt,
            CompletedAt: completedAt,
            DurationMs: DurationMs(startedAt, completedAt),
            Detail: error is null ? detail : $"{detail}; {error.GetType().Name}: {error.Message}",
            Minister: willie.Label,
            StateSource: stateSource,
            UsedRestoredSnapshot: usedRestoredSnapshot,
            TracePath: trace?.Path,
            RuleFired: trace?.RuleFired,
            EscalationReason: trace?.EscalationReason,
            AdviceCount: trace?.AdviceCount,
            FlagCount: trace?.FlagCount,
            TraceNote: trace?.Note,
            ErrorType: error?.GetType().Name ?? trace?.ErrorType,
            ErrorMessage: error?.Message ?? trace?.ErrorMessage,
            Children: []);
    }

    private static string WillieChildStepKey(AgentFlag requestFlag) =>
        $"minister_willie__{MinisterRegistry.NormalizeKey(requestFlag.SourceMinister)}__{MinisterRegistry.NormalizeKey(requestFlag.Id)}";

    private static string WillieChildLabel(AgentFlag requestFlag)
    {
        List<string> requestLabels = WillieRequestLabels(requestFlag).ToList();
        string target = requestLabels.Count switch
        {
            0 => "Willie request",
            1 => requestLabels[0],
            _ => $"{requestLabels[0]} + {requestLabels.Count - 1} more"
        };
        return $"{target} (from {requestFlag.SourceMinister})";
    }

    private static IEnumerable<string> WillieRequestLabels(AgentFlag requestFlag)
    {
        foreach (BuildingRequest request in (requestFlag.BuildingRequests ?? [])
            .Where(request => string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase)))
        {
            yield return BuildingRequestLabel(request);
        }

        foreach (ZoneRequest request in (requestFlag.ZoneRequests ?? [])
            .Where(request => string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ZoneRequestLabel(request);
        }
    }

    private static string BuildingRequestLabel(BuildingRequest request)
    {
        if (request.RoomClass is RoomClass roomClass)
            return FriendlyName(roomClass.ToString());
        if (!string.IsNullOrWhiteSpace(request.TargetDef))
            return request.TargetDef;
        return FriendlyName(request.TargetClass.ToString());
    }

    private static string ZoneRequestLabel(ZoneRequest request)
    {
        string zone = $"{FriendlyName(request.ZoneClass.ToString())} zone";
        return string.IsNullOrWhiteSpace(request.PlantDef)
            ? zone
            : $"{zone}: {request.PlantDef}";
    }

    private static string FriendlyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "request";

        StringBuilder builder = new();
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[i - 1]))
                builder.Append(' ');
            builder.Append(current);
        }

        return builder.ToString();
    }

    private IMinister? ResolveMinister(MinisterDescriptor descriptor) =>
        ministers.FirstOrDefault(m =>
            MinisterRegistry.NormalizeKey(m.Name).Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase) ||
            m.Name.Equals(descriptor.Label, StringComparison.OrdinalIgnoreCase));

    private MinisterTraceSnapshot? LatestTraceFor(MinisterDescriptor descriptor, IMinister minister) =>
        traces.Latest(descriptor.Label) ?? traces.Latest(minister.Name);

    private static string MinisterStepDetail(string minister, MinisterTraceSnapshot? trace)
    {
        if (trace is null)
            return $"{minister} completed; no trace snapshot was exposed.";

        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(trace.Path))
            parts.Add($"path {trace.Path}");
        if (!string.IsNullOrWhiteSpace(trace.RuleFired))
            parts.Add($"rule {trace.RuleFired}");
        else if (!string.IsNullOrWhiteSpace(trace.EscalationReason))
            parts.Add(trace.EscalationReason);
        if (trace.AdviceCount is not null)
            parts.Add($"{trace.AdviceCount} advice");
        if (trace.FlagCount is not null)
            parts.Add($"{trace.FlagCount} flags");
        if (!string.IsNullOrWhiteSpace(trace.ErrorType))
            parts.Add($"{trace.ErrorType}: {trace.ErrorMessage}");

        return parts.Count == 0 ? trace.Note : string.Join("; ", parts);
    }

    private static long DurationMs(DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        Math.Max(0, (long)Math.Round((completedAt - startedAt).TotalMilliseconds));
}

public sealed record MinisterTriggerResult(
    string Scope,
    string Minister,
    string Trigger,
    string RunMode,
    string StateSource,
    bool UsedRestoredSnapshot);

public sealed record CabinetTriggerResult(
    string Scope,
    string Trigger,
    string StateSource,
    bool UsedRestoredSnapshot,
    CabinetRunLogSnapshot RunLog);
