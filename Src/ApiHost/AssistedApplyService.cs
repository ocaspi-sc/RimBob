using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Aggregates;
using RimAI.Ingestion;
using RimAI.State;

namespace RimAI.Host;

public sealed class AssistedApplyService(
    AdviceBus adviceBus,
    ColonyState state,
    IngestionDispatcher ingestion,
    RimApiClient rimApi,
    ILogger<AssistedApplyService> log)
{
    private const int RecentAttemptLimit = 20;
    private readonly object _lock = new();
    private readonly List<AssistedApplyAttempt> _recentAttempts = [];

    public IReadOnlyList<AssistedApplyAttempt> LatestAttempts()
    {
        lock (_lock)
        {
            return _recentAttempts.ToArray();
        }
    }

    public async Task<AssistedApplyResponse> ApplyAsync(
        string adviceId,
        int stepIndex,
        CancellationToken ct = default)
    {
        AssistedApplyResponse result;

        if (!adviceBus.TryGetActiveAdvice(adviceId, out AdviceItem? advice) || advice is null)
        {
            result = Response("stale_advice", "That advice is no longer active.", null, adviceId, stepIndex);
            Record(result);
            return result;
        }

        if (stepIndex < 0 || stepIndex >= advice.Steps.Count)
        {
            result = Response("validation_failed", "The selected advice step no longer exists.", null, adviceId, stepIndex);
            Record(result);
            return result;
        }

        AdviceStep step = advice.Steps[stepIndex];
        AdviceStepApply? apply = step.Apply;
        if (apply is null)
        {
            result = Response("validation_failed", "That advice step is text-only and cannot be applied.", null, adviceId, stepIndex);
            Record(result);
            return result;
        }

        result = apply.Kind switch
        {
            AdviceApplyKind.MarkHarvestArea => await ApplyHarvestAsync(adviceId, stepIndex, apply, ct),
            AdviceApplyKind.UnforbidThings => await ApplyUnforbidAsync(adviceId, stepIndex, apply, ct),
            _ => Response("validation_failed", "That apply kind is not allowlisted.", apply.Kind, adviceId, stepIndex)
        };
        Record(result);
        return result;
    }

    private async Task<AssistedApplyResponse> ApplyHarvestAsync(
        string adviceId,
        int stepIndex,
        AdviceStepApply apply,
        CancellationToken ct)
    {
        if (apply.Rect is null || apply.TargetIds is null || apply.TargetIds.Count == 0)
            return Response("validation_failed", "Harvest apply is missing exact target data.", apply.Kind, adviceId, stepIndex);

        if (apply.TargetIds.Count > AssistedApplyLimits.MaxHarvestTargets ||
            apply.Rect.Area <= 0 ||
            apply.Rect.Area > AssistedApplyLimits.MaxHarvestRectArea)
        {
            return Response("validation_failed", "Harvest target is too broad for assisted apply.", apply.Kind, adviceId, stepIndex);
        }

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(apply.Kind, adviceId, stepIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, stepIndex);

        HashSet<string> targetIds = apply.TargetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<PlantRecord> currentTargets = state.Plants.Value.Plants
            .Where(plant => targetIds.Contains(plant.Id))
            .ToList();
        IReadOnlyList<PlantRecord> readyInRect = currentTargets
            .Where(plant => plant.Position is not null &&
                            plant.Growth >= 0.85f &&
                            IsInside(apply.Rect, plant.Position))
            .ToList();
        int missing = apply.TargetIds.Count - currentTargets.Count;
        if (missing > AllowedMissing(apply.TargetIds.Count))
            return Response("stale_advice", "Too many harvest targets changed since this advice was issued.", apply.Kind, adviceId, stepIndex);

        if (readyInRect.Count == 0)
            return Response("already_satisfied", "No targeted plants are currently harvest-ready.", apply.Kind, adviceId, stepIndex);

        int stale = apply.TargetIds.Count - readyInRect.Count;
        if (stale > AllowedMissing(apply.TargetIds.Count))
            return Response("stale_advice", "Too many harvest targets are no longer ready in the original area.", apply.Kind, adviceId, stepIndex);

        try
        {
            await rimApi.DesignateAreaAsync(
                apply.MapId,
                "Harvest",
                apply.Rect.X1,
                apply.Rect.Z1,
                apply.Rect.X2,
                apply.Rect.Z2,
                ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI harvest apply unavailable for advice {AdviceId} step {StepIndex}", adviceId, stepIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for harvest designation.", apply.Kind, adviceId, stepIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected harvest apply for advice {AdviceId} step {StepIndex}", adviceId, stepIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, stepIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, stepIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        return Response(
            "applied",
            $"Harvest designation submitted for {readyInRect.Count} plant target{(readyInRect.Count == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            stepIndex,
            new { target_count = readyInRect.Count, rect = apply.Rect });
    }

    private async Task<AssistedApplyResponse> ApplyUnforbidAsync(
        string adviceId,
        int stepIndex,
        AdviceStepApply apply,
        CancellationToken ct)
    {
        if (apply.ThingTargets is null || apply.ThingTargets.Count == 0)
            return Response("validation_failed", "Unforbid apply is missing exact thing ids.", apply.Kind, adviceId, stepIndex);

        if (apply.ThingTargets.Count > AssistedApplyLimits.MaxUnforbidTargets)
            return Response("validation_failed", "Unforbid target batch is too broad for assisted apply.", apply.Kind, adviceId, stepIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(apply.Kind, adviceId, stepIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, stepIndex);

        Dictionary<string, CurrentThingTarget> currentById = CurrentThingIndex();
        int missing = apply.ThingTargets.Count(target => !currentById.ContainsKey(target.Id));
        int mismatched = apply.ThingTargets.Count(target =>
            currentById.TryGetValue(target.Id, out CurrentThingTarget? current) &&
            !MatchesExpectedThing(target, current));
        if (missing + mismatched > AllowedMissing(apply.ThingTargets.Count))
            return Response("stale_advice", "Too many unforbid targets changed since this advice was issued.", apply.Kind, adviceId, stepIndex);

        IReadOnlyList<string> stillForbidden = apply.ThingTargets
            .Where(target => currentById.TryGetValue(target.Id, out CurrentThingTarget? current) &&
                             MatchesExpectedThing(target, current) &&
                             current.IsForbidden)
            .Select(target => target.Id)
            .ToList();
        if (stillForbidden.Count == 0)
            return Response("already_satisfied", "Targeted food stacks are already allowed or gone.", apply.Kind, adviceId, stepIndex);

        try
        {
            await rimApi.UnforbidThingsAsync(apply.MapId, stillForbidden, ct);
        }
        catch (RimApiHttpException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented)
        {
            log.LogWarning(ex, "RIMAPI safe unforbid endpoint is unavailable for advice {AdviceId} step {StepIndex}", adviceId, stepIndex);
            return Response("rimapi_unavailable", "Safe RIMAPI unforbid endpoint is not available yet.", apply.Kind, adviceId, stepIndex);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI unforbid apply unavailable for advice {AdviceId} step {StepIndex}", adviceId, stepIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for safe unforbid.", apply.Kind, adviceId, stepIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected unforbid apply for advice {AdviceId} step {StepIndex}", adviceId, stepIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, stepIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, stepIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        Dictionary<string, CurrentThingTarget> after = CurrentThingIndex();
        int changed = stillForbidden.Count(id => !after.TryGetValue(id, out CurrentThingTarget? current) || !current.IsForbidden);
        return Response(
            "applied",
            $"Unforbid submitted for {stillForbidden.Count} food stack{(stillForbidden.Count == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            stepIndex,
            new { requested = stillForbidden.Count, changed_or_missing = changed });
    }

    private async Task<AssistedApplyResponse?> RefreshForValidationAsync(
        AdviceApplyKind kind,
        string adviceId,
        int stepIndex,
        CancellationToken ct)
    {
        try
        {
            await ingestion.RefreshAllAsync(ct);
            return null;
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI unavailable before assisted apply validation.");
            return Response("rimapi_unavailable", "RIMAPI is unavailable, so the target could not be revalidated.", kind, adviceId, stepIndex);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Assisted apply validation refresh failed.");
            return Response("validation_failed", $"Could not refresh game state before apply: {ex.Message}", kind, adviceId, stepIndex);
        }
    }

    private async Task<AssistedApplyResponse?> RefreshForReadbackAsync(
        AdviceApplyKind kind,
        string adviceId,
        int stepIndex,
        CancellationToken ct)
    {
        try
        {
            await ingestion.RefreshAllAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Assisted apply readback refresh failed.");
            return Response("readback_inconclusive", "Apply was sent, but RimAI could not refresh readback state.", kind, adviceId, stepIndex);
        }
    }

    private Dictionary<string, CurrentThingTarget> CurrentThingIndex()
    {
        Dictionary<string, CurrentThingTarget> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (StoredResourceRecord item in state.StoredResources.Value.Items)
        {
            if (item.Position is not null)
                index[item.Id] = new CurrentThingTarget(item.Def, item.IsForbidden, item.Position);
        }

        foreach (ThingRecord thing in state.Things.Value.Things)
        {
            if (thing.Position is not null)
                index[thing.Id] = new CurrentThingTarget(thing.Def, thing.IsForbidden, thing.Position);
        }

        return index;
    }

    private static bool MatchesExpectedThing(AdviceThingApplyTarget expected, CurrentThingTarget current) =>
        string.Equals(expected.Def, current.Def, StringComparison.OrdinalIgnoreCase) &&
        expected.Position == current.Position;

    private static bool IsInside(MapRect rect, MapPosition? position) =>
        position is not null &&
        position.X >= rect.X1 &&
        position.X <= rect.X2 &&
        position.Z >= rect.Z1 &&
        position.Z <= rect.Z2;

    private static int AllowedMissing(int targetCount) =>
        (int)Math.Floor(targetCount * AssistedApplyLimits.MaxMissingTargetFraction);

    private static bool IsRimApiUnavailable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException ||
        ex is RimApiHttpException { StatusCode: null or HttpStatusCode.NotFound or HttpStatusCode.NotImplemented or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable };

    private static AssistedApplyResponse Response(
        string status,
        string message,
        AdviceApplyKind? kind,
        string adviceId,
        int stepIndex,
        object? readback = null) =>
        new(status, message, kind, adviceId, stepIndex, readback);

    private void Record(AssistedApplyResponse result)
    {
        AssistedApplyAttempt attempt = new(
            At: DateTimeOffset.UtcNow,
            Status: result.Status,
            Message: result.Message,
            Kind: result.Kind,
            AdviceId: result.AdviceId,
            StepIndex: result.StepIndex);

        lock (_lock)
        {
            _recentAttempts.Insert(0, attempt);
            if (_recentAttempts.Count > RecentAttemptLimit)
                _recentAttempts.RemoveRange(RecentAttemptLimit, _recentAttempts.Count - RecentAttemptLimit);
        }
    }
}

public sealed record AssistedApplyResponse(
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("kind")]
    AdviceApplyKind? Kind,
    [property: JsonPropertyName("advice_id")]
    string AdviceId,
    [property: JsonPropertyName("step_index")]
    int StepIndex,
    [property: JsonPropertyName("readback")]
    object? Readback = null);

public sealed record AssistedApplyAttempt(
    [property: JsonPropertyName("at")]
    DateTimeOffset At,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("kind")]
    AdviceApplyKind? Kind,
    [property: JsonPropertyName("advice_id")]
    string AdviceId,
    [property: JsonPropertyName("step_index")]
    int StepIndex);

internal sealed record CurrentThingTarget(string Def, bool IsForbidden, MapPosition Position);
