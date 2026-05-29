using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;

namespace RimBob.Ministers.Willie;

public sealed record PlacementDraft(
    string GeneratorId,
    BlueprintGroup Group,
    ResolvedAnchor SourceAnchor,
    IReadOnlyList<MapCell> AccessCells,
    IReadOnlyList<string> Assumptions,
    string ReasonSummary);

public sealed record ResolvedAnchor(
    WillieRoomAnchor Anchor,
    MapPosition TargetCell,
    AnchorMatchReason MatchReason);

public sealed record GenerationBudget(int MaxDrafts, int MaxSearchRadius);

public sealed record RectSize(int Width, int Height);

public sealed record RoomShell(
    RectSize InteriorSize,
    RectSize ExteriorSize,
    DoorSide Door,
    IReadOnlyList<TemplateAsset> Assets,
    MapCell DoorCell,
    MapCell AccessCell,
    MapCell CoolerCell);

public sealed record TemplateAsset(
    string Role,
    string DefName,
    string? StuffDefName,
    MapCell RelativeCell,
    int Rotation);

public enum DoorSide
{
    North,
    East,
    South,
    West
}

public enum AnchorMatchReason
{
    EntryCells,
    CentroidFallback,
    NoTargetCell
}
