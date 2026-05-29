using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class LargestEmptyRectangleGenerator : IPlacementGenerator
{
    public string Id => "largest_empty_rect";

    public IReadOnlyList<PlacementDraft> Generate(
        PlacementSpec spec,
        PlacementEvidence evidence,
        GenerationBudget budget)
    {
        if (budget.MaxDrafts <= 0 || budget.MaxSearchRadius < 0)
            return [];

        if (spec.RoomClass != RoomClass.Freezer && spec.TargetClass != BuildingClass.Freezer)
            return [];

        RectSize? interior = FreezerTemplate.SizeFor(spec.CapacityNeed);
        if (interior is null)
            return [];

        RectSize exterior = new(interior.Width + 2, interior.Height + 2);
        List<PlacementDraft> drafts = [];
        foreach (ResolvedAnchor anchor in evidence.Anchors)
        {
            drafts.AddRange(VariantsForAnchor(spec, evidence, budget, interior, exterior, anchor)
                .Take(budget.MaxDrafts - drafts.Count));
            if (drafts.Count >= budget.MaxDrafts) break;
        }

        return drafts;
    }

    private IReadOnlyList<PlacementDraft> VariantsForAnchor(
        PlacementSpec spec,
        PlacementEvidence evidence,
        GenerationBudget budget,
        RectSize interior,
        RectSize exterior,
        ResolvedAnchor anchor)
    {
        MapCell target = anchor.TargetCell.ToMapCell();
        List<PlacementDraft> drafts = [];
        foreach (FreeRect rect in OrderedViableRects(evidence.FreeRects, exterior, target))
        {
            foreach (MapCell origin in OriginsForRect(rect, exterior, target))
            {
                if (DistanceFromOrigin(origin, exterior, target) > budget.MaxSearchRadius)
                    continue;

                PlacementDraft? draft = TryBuildDraft(spec, evidence, interior, exterior, anchor, rect, origin);
                if (draft is null)
                    continue;

                drafts.Add(draft);
                if (drafts.Count >= budget.MaxDrafts)
                    return drafts;
            }
        }

        return drafts;
    }

    private PlacementDraft? TryBuildDraft(
        PlacementSpec spec,
        PlacementEvidence evidence,
        RectSize interior,
        RectSize exterior,
        ResolvedAnchor anchor,
        FreeRect rect,
        MapCell origin)
    {
        DoorSide door = DoorFacingAnchor(origin, exterior, anchor.TargetCell.ToMapCell());
        RoomShell shell = FreezerTemplate.BuildShell(interior, door);
        IReadOnlyList<BlueprintAsset> assets = TranslateAssets(shell, origin);
        IReadOnlyList<MapCell> accessCells =
        [
            Translate(shell.DoorCell, origin),
            Translate(shell.AccessCell, origin)
        ];
        if (!assets.All(asset => rect.Contains(asset.Cell)) ||
            !assets.All(asset => evidence.InBounds(asset.Cell)) ||
            !accessCells.All(evidence.InBounds) ||
            assets.Any(asset => evidence.IsOccupied(asset.Cell)) ||
            accessCells.Any(evidence.IsOccupied))
        {
            return null;
        }

        BlueprintGroup group = new(
            Label: LabelFor(spec),
            MapId: evidence.MapId,
            Assets: assets);
        return new PlacementDraft(
            GeneratorId: Id,
            Group: group,
            SourceAnchor: anchor,
            AccessCells: accessCells,
            Assumptions: AssumptionsFor(anchor, evidence, rect),
            ReasonSummary: $"largest empty rectangle {rect.Width}x{rect.Height} near {anchor.Anchor.Class} anchor {anchor.Anchor.RoomId}");
    }

    private static IReadOnlyList<FreeRect> OrderedViableRects(
        IReadOnlyList<FreeRect> rects,
        RectSize exterior,
        MapCell target) =>
        rects
            .Where(rect => rect.CanFit(exterior))
            .OrderByDescending(rect => rect.Area)
            .ThenBy(rect => DistanceFromRect(rect, target))
            .ThenBy(rect => rect.MinX)
            .ThenBy(rect => rect.MinZ)
            .ToList();

    private static IReadOnlyList<MapCell> OriginsForRect(
        FreeRect rect,
        RectSize exterior,
        MapCell target)
    {
        int maxX = rect.MaxXExclusive - exterior.Width;
        int maxZ = rect.MaxZExclusive - exterior.Height;
        int preferredX = Clamp(target.X - exterior.Width / 2, rect.MinX, maxX);
        int preferredZ = Clamp(target.Z - exterior.Height / 2, rect.MinZ, maxZ);
        List<MapCell> candidates =
        [
            new(preferredX, preferredZ),
            new(rect.MinX, rect.MinZ),
            new(maxX, rect.MinZ),
            new(rect.MinX, maxZ),
            new(maxX, maxZ),
            new(preferredX, rect.MinZ),
            new(preferredX, maxZ),
            new(rect.MinX, preferredZ),
            new(maxX, preferredZ)
        ];

        return candidates
            .Distinct()
            .OrderBy(origin => DistanceFromOrigin(origin, exterior, target))
            .ThenBy(origin => origin.X)
            .ThenBy(origin => origin.Z)
            .ToList();
    }

    private static IReadOnlyList<BlueprintAsset> TranslateAssets(RoomShell shell, MapCell origin) =>
        shell.Assets
            .Select(asset => new BlueprintAsset(
                Role: asset.Role,
                DefName: asset.DefName,
                StuffDefName: asset.StuffDefName,
                Cell: Translate(asset.RelativeCell, origin),
                Rotation: asset.Rotation))
            .ToList();

    private static IReadOnlyList<string> AssumptionsFor(
        ResolvedAnchor anchor,
        PlacementEvidence evidence,
        FreeRect rect)
    {
        List<string> assumptions =
        [
            $"anchor_target={anchor.MatchReason.ToString().ToLowerInvariant()}",
            "free_space=largest_empty_rectangle",
            $"free_rect={rect.Width}x{rect.Height}",
            "terrain_affordance=unknown"
        ];
        if (evidence.OccupancyIsPointApprox)
            assumptions.Add("occupancy=building_position_point_approximation");
        if (evidence.FreeSpaceScanTruncated)
            assumptions.Add("free_space_scan=truncated");
        return assumptions;
    }

    private static string LabelFor(PlacementSpec spec) =>
        spec.RoomClass == RoomClass.Freezer || spec.TargetClass == BuildingClass.Freezer
            ? "Starter freezer"
            : spec.TargetClass.ToString();

    private static DoorSide DoorFacingAnchor(MapCell origin, RectSize exterior, MapCell target)
    {
        int centerX = origin.X + exterior.Width / 2;
        int centerZ = origin.Z + exterior.Height / 2;
        int deltaX = target.X - centerX;
        int deltaZ = target.Z - centerZ;
        if (Math.Abs(deltaX) >= Math.Abs(deltaZ))
            return deltaX < 0 ? DoorSide.West : DoorSide.East;

        return deltaZ < 0 ? DoorSide.North : DoorSide.South;
    }

    private static int DistanceFromRect(FreeRect rect, MapCell target)
    {
        int clampedX = Clamp(target.X, rect.MinX, rect.MaxXExclusive - 1);
        int clampedZ = Clamp(target.Z, rect.MinZ, rect.MaxZExclusive - 1);
        return MapDistance.Manhattan(new MapPosition(clampedX, 0, clampedZ), target.ToMapPosition());
    }

    private static int DistanceFromOrigin(MapCell origin, RectSize exterior, MapCell target)
    {
        MapPosition center = new(origin.X + exterior.Width / 2, 0, origin.Z + exterior.Height / 2);
        return MapDistance.Manhattan(center, target.ToMapPosition());
    }

    private static MapCell Translate(MapCell relative, MapCell origin) =>
        new(origin.X + relative.X, origin.Z + relative.Z);

    private static int Clamp(int value, int min, int max) =>
        Math.Min(Math.Max(value, min), max);
}
