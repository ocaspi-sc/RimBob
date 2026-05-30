using RimBob.Core.Advice;
using RimBob.Core.Placement;
using RimBob.Ingestion.Dtos;

namespace RimBob.Ingestion;

public sealed class RimApiPlacementProbe(RimApiClient client) : IPlacementValidator, IPlacementPlacer, IPathCostProbe
{
    public async Task<PlacementValidationResult> ValidateAsync(
        BlueprintGroup group,
        CancellationToken ct = default)
    {
        BlueprintGroupValidateResponseDto response = await client.PostBlueprintGroupValidateAsync(
            new BlueprintGroupValidateRequestDto(
                MapId: group.MapId,
                Items: group.Assets.Select(ToDto).ToList()),
            ct);

        return ToPort(response);
    }

    public async Task<PlacementApplyResult> PlaceAsync(
        BlueprintGroup group,
        string placementOrder,
        bool requireAll,
        CancellationToken ct = default)
    {
        BlueprintGroupPlaceResultDto response = await client.PostBlueprintGroupPlaceAsync(
            new BlueprintGroupPlaceRequestDto(
                MapId: group.MapId,
                Items: group.Assets.Select(ToDto).ToList(),
                PlacementOrder: placementOrder,
                RequireAll: requireAll),
            ct);

        return new PlacementApplyResult(
            Status: response.Status,
            RequireAll: response.RequireAll,
            PlacementOrder: response.PlacementOrder,
            Items: response.Items.Select(ToPort).ToList(),
            Cost: response.Cost.Select(ToMaterialEstimate).ToList());
    }

    public async Task<IReadOnlyList<PathCostResult>> GetPathCostsAsync(
        int mapId,
        IReadOnlyList<PathCostPair> pairs,
        string tier = "region",
        string mode = "pass_doors",
        string peMode = "on_cell",
        CancellationToken ct = default)
    {
        MapPathCostBatchResponseDto response = await client.PostPathCostBatchAsync(
            new MapPathCostBatchRequestDto(
                MapId: mapId,
                Pairs: pairs.Select(pair => new MapPathCostPairRequestDto(ToDto(pair.From), ToDto(pair.To))).ToList(),
                Tier: tier,
                Mode: mode,
                PeMode: peMode),
            ct);

        return response.Results
            .Select(result => new PathCostResult(
                result.Reachable,
                result.Cost,
                ToCell(result.From),
                ToCell(result.To)))
            .ToList();
    }

    private static BlueprintGroupItemDto ToDto(BlueprintAsset asset) =>
        new(
            Role: asset.Role,
            DefName: asset.DefName,
            StuffDefName: asset.StuffDefName,
            Cell: ToDto(asset.Cell),
            Rotation: asset.Rotation);

    private static PlacementValidationResult ToPort(BlueprintGroupValidateResponseDto response) =>
        new(
            CanPlaceAll: response.CanPlaceAll,
            Items: response.Items.Select(ToPort).ToList(),
            Cost: response.Cost.Select(ToMaterialEstimate).ToList(),
            OverlapConflicts: response.OverlapConflicts.Select(ToPort).ToList());

    private static PlacementValidationItemResult ToPort(BlueprintGroupValidateItemResultDto item) =>
        new(
            Index: item.Index,
            Item: new BlueprintAsset(
                Role: item.Item.Role ?? "",
                DefName: item.Item.DefName,
                StuffDefName: item.Item.StuffDefName,
                Cell: ToCell(item.Item.Cell),
                Rotation: item.Item.Rotation),
            CanPlace: item.CanPlace,
            Reason: item.Reason,
            DefType: item.DefType,
            OccupiesCells: item.OccupiesCells.Select(ToCell).ToList(),
            Cost: item.Cost.Select(ToMaterialEstimate).ToList(),
            WorkToBuild: item.WorkToBuild,
            AlreadyBlueprinted: item.AlreadyBlueprinted,
            AlreadyBuilt: item.AlreadyBuilt);

    private static PlacementApplyItemResult ToPort(BlueprintGroupPlaceItemResultDto item) =>
        new(
            Index: item.Index,
            Item: new BlueprintAsset(
                Role: item.Item.Role ?? "",
                DefName: item.Item.DefName,
                StuffDefName: item.Item.StuffDefName,
                Cell: ToCell(item.Item.Cell),
                Rotation: item.Item.Rotation),
            Status: item.Status,
            Placed: item.Placed,
            ThingId: item.ThingId,
            Reason: item.Reason);

    private static PlacementOverlapConflict ToPort(BlueprintGroupOverlapConflictDto conflict) =>
        new(
            Cell: ToCell(conflict.Cell),
            FirstItemIndex: conflict.FirstItemIndex,
            SecondItemIndex: conflict.SecondItemIndex);

    private static MaterialEstimate ToMaterialEstimate(BlueprintCostDto cost) =>
        new(cost.DefName, cost.Count);

    private static MapCellDto ToDto(MapCell cell) =>
        new(cell.X, cell.Z);

    private static MapCell ToCell(MapCellDto cell) =>
        new(cell.X, cell.Z);
}
