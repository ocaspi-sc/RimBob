using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class ReuseExistingFootprintGenerator : IPlacementGenerator
{
    private readonly RoomTemplateSet templates;

    public ReuseExistingFootprintGenerator(RoomTemplateSet? templates = null)
    {
        this.templates = templates ?? RoomTemplateSet.Default;
    }

    public string Id => "reuse_existing_footprint";

    public IReadOnlyList<PlacementDraft> Generate(
        PlacementSpec spec,
        PlacementEvidence evidence,
        GenerationBudget budget)
    {
        if (budget.MaxDrafts <= 0)
            return [];

        IRoomTemplate? template = templates.ForSpec(spec);
        if (template is null || template.RoomClass == RoomClass.Freezer)
            return [];

        RectSize? interior = template.SizeFor(spec.CapacityNeed);
        if (interior is null)
            return [];

        RoomShell shell = template.BuildShell(interior, DoorSide.North);
        IReadOnlyList<TemplateAsset> reusableTemplateAssets = shell.Assets
            .Where(asset => IsReusableInteriorAsset(asset, interior))
            .ToList();

        HashSet<string> seenCellKeys = new(StringComparer.Ordinal);
        List<PlacementDraft> drafts = [];
        foreach (ExistingRoomFootprint footprint in MatchingFootprints(evidence, template.RoomClass))
        {
            IReadOnlyList<MapCell> origins = InteriorOrigins(footprint, interior);
            foreach (ResolvedAnchor sourceAnchor in evidence.Anchors)
            {
                foreach (MapCell origin in origins)
                {
                    PlacementDraft? draft = TryBuildDraft(evidence, template, reusableTemplateAssets, footprint, sourceAnchor, origin);
                    if (draft is null)
                        continue;

                    string cellKey = CellKey(draft);
                    if (!seenCellKeys.Add(cellKey))
                        continue;

                    drafts.Add(draft);
                    if (drafts.Count >= budget.MaxDrafts)
                        return drafts;
                }
            }
        }

        return drafts;
    }

    private PlacementDraft? TryBuildDraft(
        PlacementEvidence evidence,
        IRoomTemplate template,
        IReadOnlyList<TemplateAsset> reusableTemplateAssets,
        ExistingRoomFootprint footprint,
        ResolvedAnchor sourceAnchor,
        MapCell origin)
    {
        IReadOnlyList<BlueprintAsset> assets = ReuseAssets(reusableTemplateAssets, origin, footprint, evidence);
        if (!assets.Any(asset => !string.Equals(asset.Role, "floor", StringComparison.OrdinalIgnoreCase)))
            return null;

        IReadOnlyList<MapCell> accessCells = AccessCellsFor(footprint, sourceAnchor);
        if (accessCells.Count == 0 || !accessCells.All(evidence.InBounds))
            return null;

        BlueprintGroup group = new(
            Label: $"{template.Label} in existing room",
            MapId: evidence.MapId,
            Assets: assets);

        return new PlacementDraft(
            GeneratorId: Id,
            Group: group,
            SourceAnchor: sourceAnchor,
            AccessCells: accessCells,
            Assumptions: AssumptionsFor(sourceAnchor, footprint, evidence),
            ReasonSummary: $"{template.Label} reuses existing {template.RoomClass} room {footprint.Anchor.RoomId}");
    }

    private static IReadOnlyList<ExistingRoomFootprint> MatchingFootprints(
        PlacementEvidence evidence,
        RoomClass roomClass) =>
        evidence.RoomFootprints
            .Where(footprint => footprint.Anchor.Class == roomClass)
            .OrderByDescending(footprint => footprint.Cells.Count)
            .ThenBy(footprint => footprint.Bounds.MinX)
            .ThenBy(footprint => footprint.Bounds.MinZ)
            .ThenBy(footprint => footprint.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<MapCell> InteriorOrigins(
        ExistingRoomFootprint footprint,
        RectSize interior)
    {
        List<MapCell> origins = [];
        int maxX = footprint.Bounds.MaxXExclusive - interior.Width;
        int maxZ = footprint.Bounds.MaxZExclusive - interior.Height;
        for (int z = footprint.Bounds.MinZ; z <= maxZ; z++)
        {
            for (int x = footprint.Bounds.MinX; x <= maxX; x++)
            {
                MapCell origin = new(x, z);
                if (InteriorFits(footprint.Cells, origin, interior))
                    origins.Add(origin);
            }
        }

        return origins
            .OrderBy(origin => DistanceFromCenter(origin, interior, footprint))
            .ThenBy(origin => origin.X)
            .ThenBy(origin => origin.Z)
            .ToList();
    }

    private static bool InteriorFits(
        IReadOnlySet<MapCell> roomCells,
        MapCell origin,
        RectSize interior)
    {
        for (int z = 0; z < interior.Height; z++)
        {
            for (int x = 0; x < interior.Width; x++)
            {
                if (!roomCells.Contains(new MapCell(origin.X + x, origin.Z + z)))
                    return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<BlueprintAsset> ReuseAssets(
        IReadOnlyList<TemplateAsset> reusableTemplateAssets,
        MapCell origin,
        ExistingRoomFootprint footprint,
        PlacementEvidence evidence)
    {
        List<BlueprintAsset> assets = [];
        foreach (TemplateAsset templateAsset in reusableTemplateAssets)
        {
            MapCell cell = TranslateInterior(templateAsset.RelativeCell, origin);
            if (!footprint.Cells.Contains(cell) || !evidence.InBounds(cell) || evidence.IsOccupied(cell))
                continue;

            assets.Add(new BlueprintAsset(
                Role: templateAsset.Role,
                DefName: templateAsset.DefName,
                StuffDefName: templateAsset.StuffDefName,
                Cell: cell,
                Rotation: templateAsset.Rotation));
        }

        return assets;
    }

    private static bool IsReusableInteriorAsset(TemplateAsset asset, RectSize interior) =>
        !string.Equals(asset.Role, "wall", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(asset.Role, "door", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(asset.Role, "cooler", StringComparison.OrdinalIgnoreCase) &&
        asset.RelativeCell.X >= 1 &&
        asset.RelativeCell.X <= interior.Width &&
        asset.RelativeCell.Z >= 1 &&
        asset.RelativeCell.Z <= interior.Height;

    private static string CellKey(PlacementDraft draft) =>
        string.Join(
            ";",
            draft.Group.Assets
                .Select(asset => asset.Cell)
                .Distinct()
                .OrderBy(cell => cell.X)
                .ThenBy(cell => cell.Z)
                .Select(cell => $"{cell.X},{cell.Z}"));

    private static IReadOnlyList<MapCell> AccessCellsFor(
        ExistingRoomFootprint footprint,
        ResolvedAnchor sourceAnchor)
    {
        if (footprint.EntryCells.Count > 0)
            return footprint.EntryCells;

        if (footprint.Anchor.Centroid is not null)
            return [footprint.Anchor.Centroid.ToMapCell()];

        return [sourceAnchor.TargetCell.ToMapCell()];
    }

    private static IReadOnlyList<string> AssumptionsFor(
        ResolvedAnchor sourceAnchor,
        ExistingRoomFootprint footprint,
        PlacementEvidence evidence)
    {
        List<string> assumptions =
        [
            $"anchor_target={sourceAnchor.MatchReason.ToString().ToLowerInvariant()}",
            "room_footprint=live_rimapi_cells",
            $"reuse_room={footprint.Anchor.RoomId}",
            "reuse_scope=interior_assets_only"
        ];
        if (footprint.EntryCells.Count == 0)
            assumptions.Add("room_entry_cells=centroid_fallback");
        if (evidence.OccupancyIsPointApprox)
            assumptions.Add("occupancy=building_position_point_approximation");
        return assumptions;
    }

    private static int DistanceFromCenter(
        MapCell origin,
        RectSize interior,
        ExistingRoomFootprint footprint)
    {
        MapCell roomCenter = new(
            footprint.Bounds.MinX + footprint.Bounds.Width / 2,
            footprint.Bounds.MinZ + footprint.Bounds.Height / 2);
        MapPosition assetCenter = new(origin.X + interior.Width / 2, 0, origin.Z + interior.Height / 2);
        return MapDistance.Manhattan(assetCenter, roomCenter.ToMapPosition());
    }

    private static MapCell TranslateInterior(MapCell relative, MapCell origin) =>
        new(origin.X + relative.X - 1, origin.Z + relative.Z - 1);
}
