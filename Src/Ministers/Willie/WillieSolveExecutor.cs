using Microsoft.Extensions.Logging;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;

namespace RimBob.Ministers.Willie;

public sealed class WillieSolveExecutor(
    IPlacementSolver placementSolver,
    IGrowZonePlacementSolver growZonePlacementSolver,
    WillieSolverStore solverStore,
    ILogger<WillieSolveExecutor> log)
{
    public string BuildingFingerprint(
        BuildingRequest request,
        WillieBriefing briefing,
        ColonyState state,
        IReadOnlyList<MaterialHint> materialsOnHand) =>
        WillieSolveCacheKey.ForBuilding(request, briefing, state, materialsOnHand);

    public string ZoneFingerprint(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState state) =>
        WillieSolveCacheKey.ForZone(request, briefing, state);

    public PlacementSolveAttempt? TryUseFreshBuildingOutcome(
        string minister,
        BuildingRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        string inputFingerprint)
    {
        WillieSolverSnapshot? cachedSnapshot = solverStore.TryGetFreshBuildingOutcome(minister, request, inputFingerprint);
        CachedSolveOutcome? cachedOutcome = cachedSnapshot is null
            ? null
            : CachedSolveOutcome.From(cachedSnapshot);
        if (cachedSnapshot is null || cachedOutcome is null)
            return null;

        PlacementSolveAttempt cachedAttempt = PlacementSolveAttempt.FromCached(cachedOutcome, request);
        solverStore.RecordBuildingOutcome(cachedSnapshot with
        {
            Request = WillieSolverRequestSnapshot.FromRequest(request, sourceMinister),
            GameTick = briefing.GameTick,
            CapturedAt = DateTimeOffset.UtcNow
        }, inputFingerprint);
        return cachedAttempt;
    }

    public PlacementSolveAttempt? TryUseFreshZoneOutcome(
        string minister,
        ZoneRequest request,
        string? sourceMinister,
        WillieBriefing briefing,
        string inputFingerprint)
    {
        WillieZoneSolverSnapshot? cachedSnapshot = solverStore.TryGetFreshZoneOutcome(minister, request, inputFingerprint);
        CachedSolveOutcome? cachedOutcome = cachedSnapshot is null
            ? null
            : CachedSolveOutcome.From(cachedSnapshot);
        if (cachedSnapshot is null || cachedOutcome is null)
            return null;

        PlacementSolveAttempt cachedAttempt = PlacementSolveAttempt.FromCached(cachedOutcome, request);
        solverStore.RecordZoneOutcome(cachedSnapshot with
        {
            Request = WillieZoneRequestSnapshot.FromRequest(request, sourceMinister),
            GameTick = briefing.GameTick,
            CapturedAt = DateTimeOffset.UtcNow
        }, inputFingerprint);
        return cachedAttempt;
    }

    public async Task<PlacementSolveAttempt> SolveAndRecordAsync(
        IWillieSolveJob job,
        CancellationToken ct)
    {
        return job switch
        {
            WillieBuildingSolveJob buildingJob => await SolveAndRecordBuildingAsync(buildingJob, ct),
            WillieZoneSolveJob zoneJob => await SolveAndRecordZoneAsync(zoneJob, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(job), $"Unknown Willie solve job type {job.GetType().Name}.")
        };
    }

    public async Task<PlacementSolveAttempt> SolveAndRecordBuildingAsync(
        WillieBuildingSolveJob job,
        CancellationToken ct)
    {
        PlacementSolveAttempt attempt = await TrySolvePlacementAsync(
            job.Request,
            job.Briefing,
            job.FrozenState,
            job.MaterialsOnHand,
            ct);
        solverStore.RecordBuildingOutcome(new WillieSolverSnapshot(
            Minister: job.Minister,
            Request: WillieSolverRequestSnapshot.FromRequest(job.Request, job.SourceMinister),
            GameTick: job.GameTick,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: attempt.ReplayOutput,
            Options: attempt.Result?.Options ?? []), job.InputFingerprint);
        return attempt;
    }

    public async Task<PlacementSolveAttempt> SolveAndRecordZoneAsync(
        WillieZoneSolveJob job,
        CancellationToken ct)
    {
        PlacementSolveAttempt attempt = await TrySolveZonePlacementAsync(
            job.Request,
            job.Briefing,
            job.FrozenState,
            ct);
        solverStore.RecordZoneOutcome(new WillieZoneSolverSnapshot(
            Minister: job.Minister,
            Request: WillieZoneRequestSnapshot.FromRequest(job.Request, job.SourceMinister),
            GameTick: job.GameTick,
            CapturedAt: DateTimeOffset.UtcNow,
            Output: attempt.ReplayOutput,
            Options: attempt.Result?.Options ?? []), job.InputFingerprint);
        return attempt;
    }

    public static IReadOnlyList<MaterialHint> MaterialsOnHandFromStoredResources(ColonyState state)
    {
        IReadOnlyDictionary<string, int> countByDef = state.StoredResources.Value.CountByDef;
        if (countByDef.Count == 0) return [];

        return countByDef
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MaterialHint(pair.Key, pair.Value))
            .ToList();
    }

    private async Task<PlacementSolveAttempt> TrySolvePlacementAsync(
        BuildingRequest request,
        WillieBriefing briefing,
        ColonyState state,
        IReadOnlyList<MaterialHint> materialsOnHand,
        CancellationToken ct)
    {
        PlacementSpec spec = PlacementSpec.FromBuildingRequest(
            request,
            materialsOnHand.Count == 0 ? null : materialsOnHand);
        try
        {
            PlacementResult result = await placementSolver.SolveAsync(spec, briefing, state, ct);
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
                "Willie placement solver is offline for request={Request}; recording background failure.",
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
        ColonyState state,
        CancellationToken ct)
    {
        try
        {
            PlacementResult result = await growZonePlacementSolver.SolveAsync(request, briefing, state, ct);
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
}
