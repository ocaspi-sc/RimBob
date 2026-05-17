using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;
using RimBob.State;
using RimBob.State.Derivations.Common;

namespace RimBob.Host;

public sealed class AssistedApplyService(
    AdviceBus adviceBus,
    ColonyState state,
    IngestionDispatcher ingestion,
    RimApiClient rimApi,
    ILogger<AssistedApplyService> log)
{
    private const int RecentAttemptLimit = 20;
    private const string SimpleMealRecipeSelector = "simple_meal";
    private const string BillRepeatModeTargetCount = "TargetCount";
    private static readonly IReadOnlyList<string> SimpleMealRecipeDefs = ["CookMealSimple", "CookMealSimpleBulk"];
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
        int actionIndex,
        CancellationToken ct = default)
    {
        AssistedApplyResponse result;

        if (!adviceBus.TryGetActiveAdvice(adviceId, out AdviceItem? advice) || advice is null)
        {
            result = Response("stale_advice", "That advice is no longer active.", null, adviceId, actionIndex);
            Record(result);
            return result;
        }

        if (actionIndex < 0 || actionIndex >= advice.Actions.Count)
        {
            result = Response("validation_failed", "The selected advice action no longer exists.", null, adviceId, actionIndex);
            Record(result);
            return result;
        }

        AdviceAction action = advice.Actions[actionIndex];
        AdviceActionApply? apply = action.Apply;
        if (apply is null)
        {
            result = Response("validation_failed", "That advice action is text-only and cannot be applied.", null, adviceId, actionIndex);
            Record(result);
            return result;
        }

        result = apply.Kind switch
        {
            AdviceApplyKind.MarkHarvestArea => await ApplyHarvestAsync(adviceId, actionIndex, apply, ct),
            AdviceApplyKind.UnforbidThings => await ApplyUnforbidAsync(adviceId, actionIndex, apply, ct),
            AdviceApplyKind.UpsertProductionBill => await ApplyProductionBillAsync(adviceId, actionIndex, apply, ct),
            _ => Response("validation_failed", "That apply kind is not allowlisted.", apply.Kind, adviceId, actionIndex)
        };
        Record(result);
        return result;
    }

    private async Task<AssistedApplyResponse> ApplyProductionBillAsync(
        string adviceId,
        int actionIndex,
        AdviceActionApply apply,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apply.WorkbenchBuildingId))
            return Response("validation_failed", "Bill apply is missing the target cooking workbench.", apply.Kind, adviceId, actionIndex);

        if (!int.TryParse(apply.WorkbenchBuildingId, out int buildingId))
            return Response("validation_failed", "Bill apply has an invalid target cooking workbench id.", apply.Kind, adviceId, actionIndex);

        if (!string.Equals(apply.RecipeSelectorKey, SimpleMealRecipeSelector, StringComparison.OrdinalIgnoreCase))
            return Response("validation_failed", "Bill apply is not for the allowlisted simple-meal recipe selector.", apply.Kind, adviceId, actionIndex);

        if (!string.Equals(apply.RepeatMode, BillRepeatModeTargetCount, StringComparison.OrdinalIgnoreCase))
            return Response("validation_failed", "Bill apply is not for the allowlisted do-until target mode.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetCount < 1)
            return Response("validation_failed", "Bill target must be at least one meal.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetCount > AssistedApplyLimits.MaxProductionBillTarget)
            return Response("validation_failed", "Bill target is too high for assisted apply.", apply.Kind, adviceId, actionIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        IReadOnlyList<BuildingRecord> cookingBuildings = state.Buildings.Value.Buildings
            .Where(BuildingClassifier.IsCookingBuilding)
            .ToList();
        if (cookingBuildings.Count != 1 ||
            !string.Equals(cookingBuildings[0].Id, apply.WorkbenchBuildingId, StringComparison.OrdinalIgnoreCase))
        {
            return Response("stale_advice", "Cooking workbench target is no longer unambiguous.", apply.Kind, adviceId, actionIndex);
        }

        IReadOnlyList<WorkTableRecipeDto> recipes;
        IReadOnlyList<WorkTableBillDto> bills;
        try
        {
            recipes = await rimApi.GetWorkTableRecipesAsync(buildingId, ct);
            bills = await rimApi.GetWorkTableBillsAsync(buildingId, ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI bill surface unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for bill apply.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected bill preflight for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        WorkTableRecipeDto? recipe = ResolveSimpleMealRecipe(recipes);
        if (recipe?.DefName is null)
            return Response("validation_failed", "The targeted workbench does not expose an allowlisted simple-meal recipe.", apply.Kind, adviceId, actionIndex);

        IReadOnlyList<WorkTableBillDto> existingSimpleBills = bills
            .Where(IsSimpleMealBill)
            .ToList();
        if (existingSimpleBills.Count > 1)
            return Response("validation_failed", "Multiple simple-meal bills already exist; RimBob will not edit the player's bill stack.", apply.Kind, adviceId, actionIndex);

        WorkTableBillDto? existing = existingSimpleBills.SingleOrDefault();
        if (existing is not null &&
            string.Equals(existing.RepeatMode, BillRepeatModeTargetCount, StringComparison.OrdinalIgnoreCase) &&
            existing.TargetCount == apply.TargetCount)
        {
            return Response(
                "already_satisfied",
                $"Simple meal bill is already set to cook until {apply.TargetCount} meals.",
                apply.Kind,
                adviceId,
                actionIndex,
                new { bill_id = existing.LoadId, target_count = existing.TargetCount });
        }

        try
        {
            if (existing is null)
            {
                await rimApi.AddBillAsync(buildingId, recipe.DefName, BillRepeatModeTargetCount, apply.TargetCount, ct);
            }
            else
            {
                await rimApi.UpdateBillAsync(buildingId, existing.LoadId, BillRepeatModeTargetCount, apply.TargetCount, ct);
            }
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI bill write unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for bill apply.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected bill write for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        IReadOnlyList<WorkTableBillDto> afterBills;
        try
        {
            afterBills = await rimApi.GetWorkTableBillsAsync(buildingId, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "RIMAPI bill readback failed for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("readback_inconclusive", "Bill apply was sent, but RimBob could not read back the workbench bills.", apply.Kind, adviceId, actionIndex);
        }

        IReadOnlyList<WorkTableBillDto> afterSimpleBills = afterBills
            .Where(IsSimpleMealBill)
            .ToList();
        if (afterSimpleBills.Count != 1)
            return Response("readback_inconclusive", "Bill apply was sent, but readback did not show exactly one simple-meal bill.", apply.Kind, adviceId, actionIndex);

        WorkTableBillDto confirmed = afterSimpleBills[0];
        if (!string.Equals(confirmed.RepeatMode, BillRepeatModeTargetCount, StringComparison.OrdinalIgnoreCase) ||
            confirmed.TargetCount != apply.TargetCount)
        {
            return Response("readback_inconclusive", "Bill apply was sent, but readback did not show the expected target.", apply.Kind, adviceId, actionIndex);
        }

        string verb = existing is null ? "created" : "updated";
        return Response(
            "applied",
            $"Simple meal bill {verb} to cook until {apply.TargetCount} meals.",
            apply.Kind,
            adviceId,
            actionIndex,
            new
            {
                bill_id = confirmed.LoadId,
                recipe_def_name = confirmed.RecipeDefName,
                target_count = confirmed.TargetCount,
                repeat_mode = confirmed.RepeatMode
            });
    }

    private async Task<AssistedApplyResponse> ApplyHarvestAsync(
        string adviceId,
        int actionIndex,
        AdviceActionApply apply,
        CancellationToken ct)
    {
        if (apply.Rect is null || apply.TargetIds is null || apply.TargetIds.Count == 0)
            return Response("validation_failed", "Harvest apply is missing exact target data.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetIds.Count > AssistedApplyLimits.MaxHarvestTargets ||
            apply.Rect.Area <= 0 ||
            apply.Rect.Area > AssistedApplyLimits.MaxHarvestRectArea)
        {
            return Response("validation_failed", "Harvest target is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

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
            return Response("stale_advice", "Too many harvest targets changed since this advice was issued.", apply.Kind, adviceId, actionIndex);

        if (readyInRect.Count == 0)
            return Response("already_satisfied", "No targeted plants are currently harvest-ready.", apply.Kind, adviceId, actionIndex);

        int stale = apply.TargetIds.Count - readyInRect.Count;
        if (stale > AllowedMissing(apply.TargetIds.Count))
            return Response("stale_advice", "Too many harvest targets are no longer ready in the original area.", apply.Kind, adviceId, actionIndex);

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
            log.LogWarning(ex, "RIMAPI harvest apply unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for harvest designation.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected harvest apply for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        return Response(
            "applied",
            $"Harvest designation submitted for {readyInRect.Count} plant target{(readyInRect.Count == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            actionIndex,
            new { target_count = readyInRect.Count, rect = apply.Rect });
    }

    private async Task<AssistedApplyResponse> ApplyUnforbidAsync(
        string adviceId,
        int actionIndex,
        AdviceActionApply apply,
        CancellationToken ct)
    {
        if (apply.ThingTargets is null || apply.ThingTargets.Count == 0)
            return Response("validation_failed", "Unforbid apply is missing exact thing ids.", apply.Kind, adviceId, actionIndex);

        if (apply.ThingTargets.Count > AssistedApplyLimits.MaxUnforbidTargets)
            return Response("validation_failed", "Unforbid target batch is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        Dictionary<string, CurrentThingTarget> currentById = CurrentThingIndex();
        int missing = apply.ThingTargets.Count(target => !currentById.ContainsKey(target.Id));
        int mismatched = apply.ThingTargets.Count(target =>
            currentById.TryGetValue(target.Id, out CurrentThingTarget? current) &&
            !MatchesExpectedThing(target, current));
        if (missing + mismatched > AllowedMissing(apply.ThingTargets.Count))
            return Response("stale_advice", "Too many unforbid targets changed since this advice was issued.", apply.Kind, adviceId, actionIndex);

        IReadOnlyList<string> stillForbidden = apply.ThingTargets
            .Where(target => currentById.TryGetValue(target.Id, out CurrentThingTarget? current) &&
                             MatchesExpectedThing(target, current) &&
                             current.IsForbidden)
            .Select(target => target.Id)
            .ToList();
        if (stillForbidden.Count == 0)
            return Response("already_satisfied", "Targeted food stacks are already allowed or gone.", apply.Kind, adviceId, actionIndex);

        try
        {
            await rimApi.UnforbidThingsAsync(apply.MapId, stillForbidden, ct);
        }
        catch (RimApiHttpException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented)
        {
            log.LogWarning(ex, "RIMAPI safe unforbid endpoint is unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "Safe RIMAPI unforbid endpoint is not available yet.", apply.Kind, adviceId, actionIndex);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI unforbid apply unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for safe unforbid.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected unforbid apply for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        Dictionary<string, CurrentThingTarget> after = CurrentThingIndex();
        int changed = stillForbidden.Count(id => !after.TryGetValue(id, out CurrentThingTarget? current) || !current.IsForbidden);
        return Response(
            "applied",
            $"Unforbid submitted for {stillForbidden.Count} food stack{(stillForbidden.Count == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            actionIndex,
            new { requested = stillForbidden.Count, changed_or_missing = changed });
    }

    private async Task<AssistedApplyResponse?> RefreshForValidationAsync(
        AdviceApplyKind kind,
        string adviceId,
        int actionIndex,
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
            return Response("rimapi_unavailable", "RIMAPI is unavailable, so the target could not be revalidated.", kind, adviceId, actionIndex);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Assisted apply validation refresh failed.");
            return Response("validation_failed", $"Could not refresh game state before apply: {ex.Message}", kind, adviceId, actionIndex);
        }
    }

    private async Task<AssistedApplyResponse?> RefreshForReadbackAsync(
        AdviceApplyKind kind,
        string adviceId,
        int actionIndex,
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
            return Response("readback_inconclusive", "Apply was sent, but RimBob could not refresh readback state.", kind, adviceId, actionIndex);
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

    private static WorkTableRecipeDto? ResolveSimpleMealRecipe(IReadOnlyList<WorkTableRecipeDto> recipes)
    {
        foreach (string recipeDef in SimpleMealRecipeDefs)
        {
            WorkTableRecipeDto? recipe = recipes.FirstOrDefault(candidate =>
                string.Equals(candidate.DefName, recipeDef, StringComparison.OrdinalIgnoreCase));
            if (recipe is not null)
                return recipe;
        }

        return null;
    }

    private static bool IsSimpleMealBill(WorkTableBillDto bill) =>
        !string.IsNullOrWhiteSpace(bill.RecipeDefName) &&
        SimpleMealRecipeDefs.Any(recipeDef =>
            string.Equals(recipeDef, bill.RecipeDefName, StringComparison.OrdinalIgnoreCase));

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
        int actionIndex,
        object? readback = null) =>
        new(status, message, kind, adviceId, actionIndex, readback);

    private void Record(AssistedApplyResponse result)
    {
        AssistedApplyAttempt attempt = new(
            At: DateTimeOffset.UtcNow,
            Status: result.Status,
            Message: result.Message,
            Kind: result.Kind,
            AdviceId: result.AdviceId,
            ActionIndex: result.ActionIndex);

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
    [property: JsonPropertyName("action_index")]
    int ActionIndex,
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
    [property: JsonPropertyName("action_index")]
    int ActionIndex);

internal sealed record CurrentThingTarget(string Def, bool IsForbidden, MapPosition Position);
