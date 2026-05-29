using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Ministers.Willie;

public sealed class TemplateAnchoredGenerator : IPlacementGenerator
{
    public string Id => "template_anchored";

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

        List<PlacementDraft> drafts = [];
        foreach (ResolvedAnchor anchor in evidence.Anchors)
        {
            drafts.AddRange(VariantsForAnchor(spec, evidence, budget, interior, anchor)
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
        ResolvedAnchor anchor)
    {
        MapCell target = anchor.TargetCell.ToMapCell();
        RectSize exterior = new(interior.Width + 2, interior.Height + 2);
        List<PlacementDraft> drafts = [];
        for (int radius = 0; radius <= budget.MaxSearchRadius; radius++)
        {
            foreach (MapCell origin in RingOrigins(target, radius))
            {
                DoorSide door = DoorFacingAnchor(origin, exterior, target);
                RoomShell shell = FreezerTemplate.BuildShell(interior, door);
                IReadOnlyList<BlueprintAsset> assets = TranslateAssets(shell, origin);
                IReadOnlyList<MapCell> accessCells =
                [
                    Translate(shell.DoorCell, origin),
                    Translate(shell.AccessCell, origin)
                ];

                if (!assets.All(asset => evidence.InBounds(asset.Cell)) ||
                    !accessCells.All(evidence.InBounds) ||
                    assets.Any(asset => evidence.IsOccupied(asset.Cell)))
                {
                    continue;
                }

                BlueprintGroup group = new(
                    Label: LabelFor(spec),
                    MapId: evidence.MapId,
                    Assets: assets);
                drafts.Add(new PlacementDraft(
                    GeneratorId: Id,
                    Group: group,
                    SourceAnchor: anchor,
                    AccessCells: accessCells,
                    Assumptions: AssumptionsFor(anchor, evidence),
                    ReasonSummary: $"template variant near {anchor.Anchor.Class} anchor {anchor.Anchor.RoomId}"));
                if (drafts.Count >= budget.MaxDrafts)
                    return drafts;
            }
        }

        return drafts;
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

    private static IReadOnlyList<string> AssumptionsFor(ResolvedAnchor anchor, PlacementEvidence evidence)
    {
        List<string> assumptions =
        [
            $"anchor_target={anchor.MatchReason.ToString().ToLowerInvariant()}"
        ];
        if (evidence.OccupancyIsPointApprox)
            assumptions.Add("occupancy=building_position_point_approximation");
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

    private static IEnumerable<MapCell> RingOrigins(MapCell center, int radius)
    {
        if (radius == 0)
        {
            yield return center;
            yield break;
        }

        int minX = center.X - radius;
        int maxX = center.X + radius;
        int minZ = center.Z - radius;
        int maxZ = center.Z + radius;

        for (int x = minX; x <= maxX; x++)
            yield return new MapCell(x, minZ);

        for (int z = minZ + 1; z <= maxZ; z++)
            yield return new MapCell(maxX, z);

        for (int x = maxX - 1; x >= minX; x--)
            yield return new MapCell(x, maxZ);

        for (int z = maxZ - 1; z > minZ; z--)
            yield return new MapCell(minX, z);
    }

    private static MapCell Translate(MapCell relative, MapCell origin) =>
        new(origin.X + relative.X, origin.Z + relative.Z);
}
