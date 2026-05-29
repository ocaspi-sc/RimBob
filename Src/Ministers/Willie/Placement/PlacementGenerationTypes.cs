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

public sealed record ExistingRoomFootprint(
    WillieRoomAnchor Anchor,
    IReadOnlySet<MapCell> Cells,
    FreeRect Bounds,
    IReadOnlyList<MapCell> EntryCells);

public sealed record GenerationBudget(int MaxDrafts, int MaxSearchRadius);

public sealed record RectSize(int Width, int Height);

public sealed record FreeRect(MapCell Origin, int Width, int Height)
{
    public int MinX => Origin.X;

    public int MinZ => Origin.Z;

    public int MaxXExclusive => Origin.X + Width;

    public int MaxZExclusive => Origin.Z + Height;

    public int Area => Width * Height;

    public bool CanFit(RectSize size) =>
        Width >= size.Width && Height >= size.Height;

    public bool Contains(MapCell cell) =>
        cell.X >= MinX &&
        cell.X < MaxXExclusive &&
        cell.Z >= MinZ &&
        cell.Z < MaxZExclusive;

    public bool Contains(FreeRect other) =>
        other.MinX >= MinX &&
        other.MaxXExclusive <= MaxXExclusive &&
        other.MinZ >= MinZ &&
        other.MaxZExclusive <= MaxZExclusive;
}

public sealed record RoomShell(
    RectSize InteriorSize,
    RectSize ExteriorSize,
    DoorSide Door,
    IReadOnlyList<TemplateAsset> Assets,
    MapCell DoorCell,
    MapCell AccessCell,
    MapCell? CoolerCell);

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
