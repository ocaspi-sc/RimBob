using RimBob.Core.Advice;

namespace RimBob.Core.Placement;

public interface IPlacementValidator
{
    Task<PlacementValidationResult> ValidateAsync(
        BlueprintGroup group,
        CancellationToken ct = default);
}

public interface IPlacementPlacer
{
    Task<PlacementApplyResult> PlaceAsync(
        BlueprintGroup group,
        string placementOrder,
        bool requireAll,
        CancellationToken ct = default);
}

public interface IPathCostProbe
{
    Task<IReadOnlyList<PathCostResult>> GetPathCostsAsync(
        int mapId,
        IReadOnlyList<PathCostPair> pairs,
        string tier = "region",
        string mode = "pass_doors",
        string peMode = "on_cell",
        CancellationToken ct = default);
}

public sealed record PathCostPair(MapCell From, MapCell To);

public sealed record PathCostResult(
    bool Reachable,
    int Cost,
    MapCell From,
    MapCell To);

public sealed record PlacementValidationResult(
    bool CanPlaceAll,
    IReadOnlyList<PlacementValidationItemResult> Items,
    IReadOnlyList<MaterialEstimate> Cost,
    IReadOnlyList<PlacementOverlapConflict> OverlapConflicts);

public sealed record PlacementValidationItemResult(
    int Index,
    BlueprintAsset Item,
    bool CanPlace,
    string? Reason,
    string? DefType,
    IReadOnlyList<MapCell> OccupiesCells,
    IReadOnlyList<MaterialEstimate> Cost,
    float WorkToBuild,
    bool AlreadyBlueprinted,
    bool AlreadyBuilt);

public sealed record PlacementOverlapConflict(
    MapCell Cell,
    int FirstItemIndex,
    int SecondItemIndex);

public sealed record PlacementApplyResult(
    string Status,
    bool RequireAll,
    string PlacementOrder,
    IReadOnlyList<PlacementApplyItemResult> Items,
    IReadOnlyList<MaterialEstimate> Cost);

public sealed record PlacementApplyItemResult(
    int Index,
    BlueprintAsset Item,
    string Status,
    bool Placed,
    int? ThingId,
    string? Reason);
