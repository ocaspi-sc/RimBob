using RimBob.Core.Advice;
using RimBob.Core.Placement;
using RimBob.Ingestion.Dtos;

namespace RimBob.Ingestion;

public sealed class RimApiPlacementProbe(RimApiClient client) : IPlacementValidator, IPathCostProbe
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

        return new PlacementValidationResult(
            CanPlaceAll: response.CanPlaceAll,
            Items: response.Items.Select(ToPort).ToList(),
            Cost: response.Cost.Select(ToMaterialEstimate).ToList(),
            OverlapConflicts: response.OverlapConflicts.Select(ToPort).ToList());
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
