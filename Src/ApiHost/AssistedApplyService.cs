using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Placement;
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
    IPlacementValidator placementValidator,
    IPlacementPlacer placementPlacer,
    ILogger<AssistedApplyService> log)
{
    private const int RecentAttemptLimit = 20;
    private const string SimpleMealRecipeSelector = "simple_meal";
    private const string BillRepeatModeTargetCount = "TargetCount";
    private const string BlueprintGroupPlacementOrder = "default";
    private const bool BlueprintGroupRequireAll = true;
    private const int GrowingZoneReadbackRefreshLimit = 3;
    private static readonly TimeSpan GrowingZoneReadbackRefreshDelay = TimeSpan.FromMilliseconds(100);
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

    public static HarvestApplyAssessment AssessHarvest(ColonyState state, MarkHarvestAreaApply apply)
    {
        if (apply.TargetIds.Count > AssistedApplyLimits.MaxHarvestTargets ||
            apply.Rect.Area <= 0 ||
            apply.Rect.Area > AssistedApplyLimits.MaxHarvestRectArea)
        {
            return new HarvestApplyAssessment(
                HarvestApplyOutcome.TooBroad,
                ReadyCount: 0,
                MissingCount: 0,
                StaleCount: apply.TargetIds.Count);
        }

        if (apply.MapId != state.Map.Value.Id)
        {
            return new HarvestApplyAssessment(
                HarvestApplyOutcome.WrongMap,
                ReadyCount: 0,
                MissingCount: 0,
                StaleCount: 0);
        }

        HashSet<string> targetIds = apply.TargetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<PlantRecord> currentTargets = state.Plants.Value.Plants
            .Where(plant => targetIds.Contains(plant.Id))
            .ToList();
        IReadOnlyList<PlantRecord> readyInRect = currentTargets
            .Where(plant => plant.Position is not null &&
                            PlantHarvest.IsReady(plant) &&
                            IsInside(apply.Rect, plant.Position))
            .ToList();

        int missing = apply.TargetIds.Count - currentTargets.Count;
        int stale = apply.TargetIds.Count - readyInRect.Count;
        if (missing > AllowedMissing(apply.TargetIds.Count))
        {
            return new HarvestApplyAssessment(
                HarvestApplyOutcome.StaleMissing,
                readyInRect.Count,
                missing,
                stale);
        }

        if (readyInRect.Count == 0)
        {
            return new HarvestApplyAssessment(
                HarvestApplyOutcome.AlreadySatisfied,
                0,
                missing,
                stale);
        }

        if (stale > AllowedMissing(apply.TargetIds.Count))
        {
            return new HarvestApplyAssessment(
                HarvestApplyOutcome.StaleNotReady,
                readyInRect.Count,
                missing,
                stale);
        }

        return new HarvestApplyAssessment(
            HarvestApplyOutcome.Ready,
            readyInRect.Count,
            missing,
            stale);
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

        AssistedApplyResponse? staleAdvice = StaleAdviceIfExpired(advice, null, adviceId, actionIndex);
        if (staleAdvice is not null)
        {
            Record(staleAdvice);
            return staleAdvice;
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

        result = apply switch
        {
            MarkHarvestAreaApply harvest => await ApplyHarvestAsync(advice, adviceId, actionIndex, harvest, ct),
            MarkHuntAreaApply hunt => await ApplyHuntAsync(advice, adviceId, actionIndex, hunt, ct),
            UnforbidThingsApply unforbid => await ApplyUnforbidAsync(advice, adviceId, actionIndex, unforbid, ct),
            UpsertProductionBillApply bill => await ApplyProductionBillAsync(advice, adviceId, actionIndex, bill, ct),
            PlaceBlueprintGroupApply blueprint => await ApplyBlueprintGroupAsync(advice, adviceId, actionIndex, blueprint, ct),
            CreateGrowingZoneApply growZone => await ApplyCreateGrowingZoneAsync(advice, adviceId, actionIndex, growZone, ct),
            _ => Response("validation_failed", "That apply kind is not allowlisted.", apply.Kind, adviceId, actionIndex)
        };
        if (ShouldMarkAppliedAction(result))
            adviceBus.MarkActionApplied(adviceId, actionIndex, ApplyResult(result));
        Record(result);
        return result;
    }

    private async Task<AssistedApplyResponse> ApplyProductionBillAsync(
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        UpsertProductionBillApply apply,
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

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
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
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        MarkHarvestAreaApply apply,
        CancellationToken ct)
    {
        if (apply.TargetIds.Count == 0)
            return Response("validation_failed", "Harvest apply is missing exact target data.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetIds.Count > AssistedApplyLimits.MaxHarvestTargets ||
            apply.Rect.Area <= 0 ||
            apply.Rect.Area > AssistedApplyLimits.MaxHarvestRectArea)
        {
            return Response("validation_failed", "Harvest target is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        HarvestApplyAssessment assessment = AssessHarvest(state, apply);
        if (assessment.Outcome == HarvestApplyOutcome.WrongMap)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == HarvestApplyOutcome.StaleMissing)
            return Response("stale_advice", "Too many harvest targets changed since this advice was issued.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == HarvestApplyOutcome.AlreadySatisfied)
            return Response("already_satisfied", "No targeted plants are currently harvest-ready.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == HarvestApplyOutcome.StaleNotReady)
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
            $"Harvest designation submitted for {assessment.ReadyCount} plant target{(assessment.ReadyCount == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            actionIndex,
            new { target_count = assessment.ReadyCount, rect = apply.Rect });
    }

    private async Task<AssistedApplyResponse> ApplyHuntAsync(
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        MarkHuntAreaApply apply,
        CancellationToken ct)
    {
        if (apply.TargetIds.Count == 0)
            return Response("validation_failed", "Hunt apply is missing exact target data.", apply.Kind, adviceId, actionIndex);

        HashSet<string> targetIds = apply.TargetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targetIds.Count != apply.TargetIds.Count)
            return Response("validation_failed", "Hunt apply has duplicate target ids.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetIds.Count > AssistedApplyLimits.MaxHuntTargets ||
            apply.Rect.Area <= 0 ||
            apply.Rect.Area > AssistedApplyLimits.MaxHuntRectArea)
        {
            return Response("validation_failed", "Hunt target is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        IReadOnlyList<AnimalRecord> knownTargets = state.Animals.Value.Animals
            .Where(animal => targetIds.Contains(animal.Id))
            .ToList();
        if (knownTargets.Count == 0)
            return Response("already_satisfied", "No targeted animals are currently available for hunting.", apply.Kind, adviceId, actionIndex);

        IReadOnlyList<AnimalRecord> currentTargets = knownTargets
            .Where(animal => FoodHuntSafety.IsLowRiskTarget(animal, state.AnimalDefs.Value))
            .ToList();
        int missing = apply.TargetIds.Count - currentTargets.Count;
        if (missing > AllowedMissing(apply.TargetIds.Count))
            return Response("stale_advice", "Too many hunt targets changed since this advice was issued.", apply.Kind, adviceId, actionIndex);

        try
        {
            await rimApi.DesignateHuntThingsAsync(
                apply.MapId,
                currentTargets.Select(animal => animal.Id).ToList(),
                ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI hunt apply unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for hunt designation.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected hunt apply for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
        if (readbackFailure is not null)
            return readbackFailure;

        return Response(
            "applied",
            $"Hunt designation submitted for {currentTargets.Count} animal target{(currentTargets.Count == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            actionIndex,
            new { target_count = currentTargets.Count, rect = apply.Rect });
    }

    private async Task<AssistedApplyResponse> ApplyUnforbidAsync(
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        UnforbidThingsApply apply,
        CancellationToken ct)
    {
        if (apply.ThingTargets.Count == 0)
            return Response("validation_failed", "Unforbid apply is missing exact thing ids.", apply.Kind, adviceId, actionIndex);

        if (apply.ThingTargets.Count > AssistedApplyLimits.MaxUnforbidTargets)
            return Response("validation_failed", "Unforbid target batch is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
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

    private async Task<AssistedApplyResponse> ApplyBlueprintGroupAsync(
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        PlaceBlueprintGroupApply apply,
        CancellationToken ct)
    {
        int actualAssetCount = apply.BlueprintGroup.Assets.Count;
        if (actualAssetCount == 0)
            return Response("validation_failed", "Blueprint group apply is missing blueprint assets.", apply.Kind, adviceId, actionIndex);

        if (apply.AssetCount != actualAssetCount)
            return Response("validation_failed", "Blueprint group apply asset count does not match its payload.", apply.Kind, adviceId, actionIndex);

        if (actualAssetCount > AssistedApplyLimits.MaxBlueprintGroupAssets)
            return Response("validation_failed", "Blueprint group is too large for assisted apply.", apply.Kind, adviceId, actionIndex);

        if (apply.BlueprintGroup.MapId != apply.MapId)
            return Response("validation_failed", "Blueprint group apply map id does not match its payload.", apply.Kind, adviceId, actionIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        if (apply.MapId != state.Map.Value.Id)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        PlacementValidationResult validation;
        try
        {
            validation = await placementValidator.ValidateAsync(apply.BlueprintGroup, ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI blueprint group validate unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for blueprint group validation.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected blueprint group validation for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        if (!validation.CanPlaceAll)
        {
            string reason = FirstBlueprintValidationFailure(validation) ?? "RIMAPI validation rejected the blueprint group.";
            return Response("stale_advice", $"Blueprint group no longer validates: {reason}", apply.Kind, adviceId, actionIndex);
        }

        PlacementApplyResult placeResult;
        try
        {
            placeResult = await placementPlacer.PlaceAsync(
                apply.BlueprintGroup,
                BlueprintGroupPlacementOrder,
                BlueprintGroupRequireAll,
                ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI blueprint group place unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for blueprint group placement.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected blueprint group placement for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        if (placeResult.Items.Count == 0)
            return Response("rimapi_rejected", "RIMAPI returned no blueprint placement results.", apply.Kind, adviceId, actionIndex);

        int placedCount = placeResult.Items.Count(IsBlueprintPlaced);
        int alreadyPresentCount = placeResult.Items.Count(IsBlueprintAlreadyPresent);
        int failedCount = placeResult.Items.Count(IsBlueprintFailed);
        object readback = BlueprintGroupReadback(placeResult, placedCount, alreadyPresentCount, failedCount);

        if (placedCount > 0 || alreadyPresentCount > 0)
        {
            AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
            if (readbackFailure is not null)
                return readbackFailure;
        }

        if (failedCount > 0 && (placedCount > 0 || alreadyPresentCount > 0))
        {
            return Response(
                "readback_inconclusive",
                "Blueprint group placement was partially applied; inspect the map before retrying.",
                apply.Kind,
                adviceId,
                actionIndex,
                readback);
        }

        if (placedCount > 0)
        {
            return Response(
                "applied",
                $"Blueprint group placed {placedCount} asset{(placedCount == 1 ? "" : "s")}.",
                apply.Kind,
                adviceId,
                actionIndex,
                readback);
        }

        if (alreadyPresentCount == placeResult.Items.Count)
        {
            return Response(
                "already_satisfied",
                "Blueprint group is already present on the map.",
                apply.Kind,
                adviceId,
                actionIndex,
                readback);
        }

        return Response(
            "rimapi_rejected",
            FirstBlueprintPlaceFailure(placeResult) ?? "RIMAPI rejected blueprint group placement.",
            apply.Kind,
            adviceId,
            actionIndex,
            readback);
    }

    private async Task<AssistedApplyResponse> ApplyCreateGrowingZoneAsync(
        AdviceItem advice,
        string adviceId,
        int actionIndex,
        CreateGrowingZoneApply apply,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apply.PlantDef))
            return Response("validation_failed", "Growing-zone apply is missing the plant def.", apply.Kind, adviceId, actionIndex);

        if (apply.TargetCount < 1)
            return Response("validation_failed", "Growing-zone target count must be positive.", apply.Kind, adviceId, actionIndex);

        if (apply.Rect.Area <= 0)
            return Response("validation_failed", "Growing-zone rect is invalid.", apply.Kind, adviceId, actionIndex);

        if (apply.Rect.Area > AssistedApplyLimits.MaxGrowingZoneCells ||
            apply.TargetCount > AssistedApplyLimits.MaxGrowingZoneCells)
        {
            return Response("validation_failed", "Growing-zone target is too broad for assisted apply.", apply.Kind, adviceId, actionIndex);
        }

        if (apply.TargetCount != apply.Rect.Area)
            return Response("validation_failed", "Growing-zone target count does not match its rect.", apply.Kind, adviceId, actionIndex);

        AssistedApplyResponse? refreshFailure = await RefreshForValidationAsync(advice, apply.Kind, adviceId, actionIndex, ct);
        if (refreshFailure is not null)
            return refreshFailure;

        HashSet<string> growingZoneIdsBeforeWrite = GrowingZoneIds(state);
        GrowZoneApplyAssessment assessment = AssessGrowingZone(state, apply);
        if (assessment.Outcome == GrowZoneApplyOutcome.WrongMap)
            return Response("stale_advice", "Advice targets a different map than the current colony map.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.UnsupportedPlantDef)
            return Response("validation_failed", $"Plant def {apply.PlantDef} is not present as a plant in the live def catalogue.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.NoTerrainGrid)
            return Response("stale_advice", "Cell-level terrain is unavailable; RimBob cannot safely validate the growing-zone rectangle.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.RectOutsideTerrain)
            return Response("stale_advice", "Growing-zone rectangle is outside the current terrain bounds.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.AlreadySatisfied)
            return Response("already_satisfied", "A matching growing zone already covers the requested rectangle.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.NotGrowable)
            return Response("stale_advice", "One or more growing-zone cells no longer support growing.", apply.Kind, adviceId, actionIndex);

        if (assessment.Outcome == GrowZoneApplyOutcome.BlockedOrZoned)
            return Response("stale_advice", "One or more growing-zone cells are now occupied or already zoned.", apply.Kind, adviceId, actionIndex);

        try
        {
            await rimApi.CreateGrowZoneAsync(
                apply.MapId,
                apply.PlantDef,
                apply.Rect.X1,
                apply.Rect.Z1,
                apply.Rect.X2,
                apply.Rect.Z2,
                ct);
        }
        catch (Exception ex) when (IsRimApiUnavailable(ex))
        {
            log.LogWarning(ex, "RIMAPI growing-zone apply unavailable for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_unavailable", "RIMAPI is unavailable for growing-zone creation.", apply.Kind, adviceId, actionIndex);
        }
        catch (RimApiException ex)
        {
            log.LogWarning(ex, "RIMAPI rejected growing-zone apply for advice {AdviceId} action {ActionIndex}", adviceId, actionIndex);
            return Response("rimapi_rejected", ex.Message, apply.Kind, adviceId, actionIndex);
        }

        MapZoneRecord? createdZone = null;
        for (int attempt = 1; attempt <= GrowingZoneReadbackRefreshLimit; attempt++)
        {
            AssistedApplyResponse? readbackFailure = await RefreshForReadbackAsync(apply.Kind, adviceId, actionIndex, ct);
            if (readbackFailure is not null)
                return readbackFailure;

            createdZone = NewMatchingGrowingZone(state, growingZoneIdsBeforeWrite, apply);
            if (createdZone is not null)
                break;

            if (attempt < GrowingZoneReadbackRefreshLimit)
            {
                // RIMAPI queues zone creation onto RimWorld's main thread; the first refresh can beat registration.
                await Task.Delay(GrowingZoneReadbackRefreshDelay, ct);
            }
        }

        if (createdZone is null)
        {
            return Response(
                "readback_inconclusive",
                "Growing-zone creation was accepted by RIMAPI, but readback did not show the new zone yet; it may still be queued on RimWorld's main thread.",
                apply.Kind,
                adviceId,
                actionIndex,
                new { plant_def = apply.PlantDef, rect = apply.Rect, target_count = apply.TargetCount, readback_attempts = GrowingZoneReadbackRefreshLimit });
        }

        return Response(
            "applied",
            $"Growing zone created for {apply.TargetCount} {apply.PlantDef} tile{(apply.TargetCount == 1 ? "" : "s")}.",
            apply.Kind,
            adviceId,
            actionIndex,
            new { plant_def = apply.PlantDef, rect = apply.Rect, target_count = apply.TargetCount, zone_id = createdZone.Id });
    }

    private static GrowZoneApplyAssessment AssessGrowingZone(ColonyState state, CreateGrowingZoneApply apply)
    {
        if (apply.MapId != state.Map.Value.Id)
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.WrongMap);

        if (!state.ThingDefs.Value.DefsByName.TryGetValue(apply.PlantDef, out ThingDefRecord? plantDef) ||
            !plantDef.IsPlant)
        {
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.UnsupportedPlantDef);
        }

        TerrainSnapshot terrain = state.Terrain.Value;
        if (!terrain.HasCoordinateGrid)
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.NoTerrainGrid);

        if (!RectInsideTerrain(apply.Rect, terrain))
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.RectOutsideTerrain);

        if (HasMatchingGrowingZone(state, apply))
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.AlreadySatisfied);

        Dictionary<MapCell, TerrainCellRecord> terrainByCell = terrain.Cells
            .ToDictionary(cell => new MapCell(cell.X, cell.Z));
        IReadOnlyList<MapCell> targetCells = CellsIn(apply.Rect);
        if (targetCells.Any(cell => !terrainByCell.TryGetValue(cell, out TerrainCellRecord? terrainCell) || !terrainCell.SupportsGrowing))
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.NotGrowable);

        HashSet<MapCell> blockedCells = GrowZoneBlockedCells(state);
        if (targetCells.Any(blockedCells.Contains))
            return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.BlockedOrZoned);

        return new GrowZoneApplyAssessment(GrowZoneApplyOutcome.Ready);
    }

    private static bool HasMatchingGrowingZone(ColonyState state, CreateGrowingZoneApply apply)
    {
        foreach (MapZoneRecord zone in state.Zones.Value.Zones.Where(zone => zone.IsGrowing))
        {
            if (!string.Equals(zone.PlantDef, apply.PlantDef, StringComparison.OrdinalIgnoreCase))
                continue;

            if (zone.CellCount == apply.TargetCount)
                return true;
        }

        return false;
    }

    private static HashSet<string> GrowingZoneIds(ColonyState state) =>
        state.Zones.Value.Zones
            .Where(zone => zone.IsGrowing && !string.IsNullOrWhiteSpace(zone.Id))
            .Select(zone => zone.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static MapZoneRecord? NewMatchingGrowingZone(
        ColonyState state,
        HashSet<string> beforeWriteIds,
        CreateGrowingZoneApply apply)
    {
        foreach (MapZoneRecord zone in state.Zones.Value.Zones.Where(zone => zone.IsGrowing))
        {
            if (string.IsNullOrWhiteSpace(zone.Id))
                continue;

            if (beforeWriteIds.Contains(zone.Id))
                continue;

            if (zone.CellCount != apply.TargetCount)
                continue;

            if (!string.IsNullOrWhiteSpace(zone.PlantDef) &&
                !string.Equals(zone.PlantDef, apply.PlantDef, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return zone;
        }

        return null;
    }

    private static HashSet<MapCell> GrowZoneBlockedCells(ColonyState state)
    {
        HashSet<MapCell> cells = [];
        foreach (MapZoneRecord zone in state.Zones.Value.Zones)
            AddCells(cells, zone.Cells);
        foreach (StockpileZone stockpile in state.Stockpiles.Value.Zones)
        {
            AddCells(cells, stockpile.Cells);
            AddCell(cells, stockpile.Center);
        }
        foreach (BuildingRecord building in state.Buildings.Value.Buildings)
            AddCell(cells, building.Position);
        foreach (RoomRecord room in state.Rooms.Value.Rooms)
            AddCells(cells, room.Cells);
        foreach (PlantRecord plant in state.Plants.Value.Plants.Where(plant => plant.IsCrop))
            AddCell(cells, plant.Position);

        return cells;
    }

    private static IReadOnlyList<MapCell> CellsIn(MapRect rect)
    {
        List<MapCell> cells = [];
        for (int z = rect.Z1; z <= rect.Z2; z++)
        {
            for (int x = rect.X1; x <= rect.X2; x++)
                cells.Add(new MapCell(x, z));
        }

        return cells;
    }

    private static void AddCells(HashSet<MapCell> cells, IReadOnlyList<MapPosition> positions)
    {
        foreach (MapPosition position in positions)
            cells.Add(position.ToMapCell());
    }

    private static void AddCell(HashSet<MapCell> cells, MapPosition? position)
    {
        if (position is not null)
            cells.Add(position.ToMapCell());
    }

    private static bool RectInsideTerrain(MapRect rect, TerrainSnapshot terrain) =>
        rect.X1 >= 0 &&
        rect.Z1 >= 0 &&
        rect.X2 < terrain.Width &&
        rect.Z2 < terrain.Height;

    private static string? FirstBlueprintValidationFailure(PlacementValidationResult validation)
    {
        if (validation.OverlapConflicts.Count > 0)
            return "blueprint group items overlap each other";

        PlacementValidationItemResult? failed = validation.Items.FirstOrDefault(item =>
            !item.CanPlace &&
            !item.AlreadyBlueprinted &&
            !item.AlreadyBuilt);
        return failed?.Reason;
    }

    private static string? FirstBlueprintPlaceFailure(PlacementApplyResult result) =>
        result.Items.FirstOrDefault(IsBlueprintFailed)?.Reason;

    private static bool IsBlueprintPlaced(PlacementApplyItemResult item) =>
        item.Placed ||
        string.Equals(item.Status, "placed", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlueprintAlreadyPresent(PlacementApplyItemResult item) =>
        string.Equals(item.Status, "already_present", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlueprintFailed(PlacementApplyItemResult item) =>
        string.Equals(item.Status, "rejected", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Status, "not_placed", StringComparison.OrdinalIgnoreCase);

    private static object BlueprintGroupReadback(
        PlacementApplyResult result,
        int placedCount,
        int alreadyPresentCount,
        int failedCount) =>
        new
        {
            status = result.Status,
            require_all = result.RequireAll,
            placement_order = result.PlacementOrder,
            placed_count = placedCount,
            already_present_count = alreadyPresentCount,
            failed_count = failedCount,
            cost = result.Cost,
            items = result.Items.Select(item => new
            {
                index = item.Index,
                status = item.Status,
                placed = item.Placed,
                thing_id = item.ThingId,
                reason = item.Reason
            }).ToList()
        };

    private async Task<AssistedApplyResponse?> RefreshForValidationAsync(
        AdviceItem advice,
        AdviceApplyKind kind,
        string adviceId,
        int actionIndex,
        CancellationToken ct)
    {
        try
        {
            await ingestion.RefreshAllAsync(ct);
            return StaleAdviceIfExpired(advice, kind, adviceId, actionIndex);
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

    private AssistedApplyResponse? StaleAdviceIfExpired(
        AdviceItem advice,
        AdviceApplyKind? kind,
        string adviceId,
        int actionIndex)
    {
        long currentGameTick = state.Economy.Value.Tick;
        if (!AdviceFreshness.IsExpiredForGameTick(advice, currentGameTick))
            return null;

        string message = advice.ExpiresGameTick is { } expiresGameTick
            ? $"That advice expired at game tick {expiresGameTick}; current tick is {currentGameTick}."
            : "That advice is no longer current.";
        return Response("stale_advice", message, kind, adviceId, actionIndex);
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

    private static bool ShouldMarkAppliedAction(AssistedApplyResponse result) =>
        string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Status, "already_satisfied", StringComparison.OrdinalIgnoreCase);

    private static AdviceActionApplyResult ApplyResult(AssistedApplyResponse result) =>
        new(result.Status, result.Message, result.Kind, DateTimeOffset.UtcNow);

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

public enum HarvestApplyOutcome
{
    Ready,
    AlreadySatisfied,
    StaleMissing,
    StaleNotReady,
    WrongMap,
    TooBroad
}

public readonly record struct HarvestApplyAssessment(
    HarvestApplyOutcome Outcome,
    int ReadyCount,
    int MissingCount,
    int StaleCount);

public enum GrowZoneApplyOutcome
{
    Ready,
    AlreadySatisfied,
    WrongMap,
    UnsupportedPlantDef,
    NoTerrainGrid,
    RectOutsideTerrain,
    NotGrowable,
    BlockedOrZoned
}

public readonly record struct GrowZoneApplyAssessment(GrowZoneApplyOutcome Outcome);

internal sealed record CurrentThingTarget(string Def, bool IsForbidden, MapPosition Position);
