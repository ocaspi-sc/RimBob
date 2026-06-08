using System.Globalization;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;

namespace RimBob.Ministers.Willie;

public interface IGrowZonePlacementSolver
{
    Task<PlacementResult> SolveAsync(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState colonyState,
        CancellationToken ct = default);
}

public sealed class GrowZonePlacementSolver : IGrowZonePlacementSolver
{
    private const int DefaultTileCount = 36;
    private const int MaxTileCount = 120;
    private const int MaxOptionCount = 3;

    public Task<PlacementResult> SolveAsync(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState colonyState,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        List<string> notes = [];
        List<PlacementDraftTrace> traces = [];
        if (request.ZoneClass != ZoneClass.Growing)
        {
            return Task.FromResult(NoFit(
                NoFitReason.UnsupportedZoneClass,
                traces,
                notes,
                $"unsupported zone class {request.ZoneClass}"));
        }

        TerrainSnapshot terrain = colonyState.Terrain.Value;
        if (!terrain.HasCoordinateGrid)
        {
            return Task.FromResult(NoFit(
                NoFitReason.NoTerrainGrid,
                traces,
                notes,
                "terrain snapshot has no coordinate-addressable cell grid"));
        }

        SearchContext searchContext = BuildSearchContext(terrain, colonyState.Areas.Value, notes);
        MapRect searchBounds = searchContext.Bounds;
        TerrainIndex terrainIndex = TerrainIndex.Build(terrain, request);
        HashSet<MapCell> blockedCells = BuildZoneBlockedMask(request, terrain, colonyState, searchBounds);
        IReadOnlyList<ResolvedAnchor> anchors = ResolveZoneAnchors(request, briefing, colonyState, searchContext);
        PlacementEvidence.FreeRectScanResult freeSpace = PlacementEvidence.BuildFreeRects(searchBounds, blockedCells.Contains);
        if (freeSpace.ScanTruncated)
            notes.Add("grow-zone free-space scan exceeded the bounded scan budget");

        IReadOnlyList<ZoneCandidate> candidates = BuildCandidatesFromFreeRects(
            request,
            briefing.MapId,
            terrainIndex,
            freeSpace.Rects,
            anchors);

        if (candidates.Count == 0)
        {
            bool anyGrowable = terrain.Cells.Any(cell =>
                CellSupportsTerrainNeed(request, cell) &&
                Inside(cell.X, cell.Z, searchBounds));
            bool anyUnblockedGrowable = terrain.Cells.Any(cell =>
                CellSupportsTerrainNeed(request, cell) &&
                Inside(cell.X, cell.Z, searchBounds) &&
                !blockedCells.Contains(new MapCell(cell.X, cell.Z)));
            NoFitReason reason = !anyGrowable
                ? NoFitReason.NoGrowableCells
                : !anyUnblockedGrowable
                    ? NoFitReason.AllZoneCellsBlocked
                    : NoFitReason.NoZoneRectangle;
            return Task.FromResult(NoFit(
                reason,
                traces,
                notes,
                reason switch
                {
                    NoFitReason.NoGrowableCells => "no growable terrain cells were available inside the search bounds",
                    NoFitReason.AllZoneCellsBlocked => "all growable cells inside the search bounds were blocked by existing zones or occupancy",
                    _ => "no compact rectangle satisfied growable, unoccupied, unzoned cells"
                }));
        }

        IReadOnlyList<ZoneCandidate> selected = SelectOptions(candidates);
        traces.AddRange(candidates.Take(12).Select(candidate => TraceFor(candidate, "scored", null)));
        traces.AddRange(selected.Select(candidate => TraceFor(candidate, "selected", null)));

        IReadOnlyList<AdviceOption> options = selected
            .Select(candidate => AssembleOption(request, candidate))
            .ToList();
        return Task.FromResult(new PlacementResult(
            Options: options,
            Trace: new PlacementTrace("grow_zone_placement_solver", traces, notes),
            NoFit: null,
            Draftable: PlacementReadiness.Ready,
            PlacementValid: PlacementReadiness.Ready,
            MaterialsReady: PlacementReadiness.Ready,
            ApplyReady: PlacementReadiness.Ready));
    }

    private static SearchContext BuildSearchContext(
        TerrainSnapshot terrain,
        MapAreaRegistry areas,
        List<string> notes)
    {
        MapRect terrainBounds = new(0, 0, terrain.Width - 1, terrain.Height - 1);
        MapArea? home = areas.Areas
            .Where(area => string.Equals(area.Type, "Area_Home", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(area.Type, "Home", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(area => area.CellCount)
            .ThenBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        WillieRoomAnchor? fallbackAnchor = BuildHomeAnchor(home, terrainBounds, notes);
        MapPosition searchCenter = fallbackAnchor?.Centroid ?? CenterOf(terrainBounds);
        MapRect searchBounds = BudgetedSearchBounds(terrainBounds, searchCenter, notes);
        return new SearchContext(searchBounds, fallbackAnchor);
    }

    private static WillieRoomAnchor? BuildHomeAnchor(
        MapArea? home,
        MapRect terrainBounds,
        List<string> notes)
    {
        if (home is null)
        {
            notes.Add("no Home area available; grow-zone search uses terrain bounds with map-center fallback");
            return null;
        }

        MapRect? bounds = home.Bounds is null ? null : Clamp(home.Bounds, terrainBounds);
        if (bounds is null)
            notes.Add("Home area row did not include bounds; using Home centroid as the grow-zone anchor");
        else if (home.Cells.Count == 0)
            notes.Add("Home area row did not include cells; using Home bounds center as the grow-zone anchor");

        MapPosition centroid = home.Centroid ?? (bounds is null ? CenterOf(terrainBounds) : CenterOf(bounds));
        return new WillieRoomAnchor(
            $"area:{home.Id}",
            RoomClass.BuildableRegion,
            home.Label ?? "Home area",
            home.CellCount,
            centroid,
            [])
        {
            Bounds = bounds,
            Cells = home.Cells
        };
    }

    private static MapRect BudgetedSearchBounds(
        MapRect terrainBounds,
        MapPosition center,
        List<string> notes)
    {
        if (terrainBounds.Area <= PlacementEvidence.MaxFreeSpaceScanCells)
            return terrainBounds;

        int terrainWidth = terrainBounds.X2 - terrainBounds.X1 + 1;
        int terrainHeight = terrainBounds.Z2 - terrainBounds.Z1 + 1;
        double terrainRatio = terrainWidth / (double)Math.Max(1, terrainHeight);
        int searchWidth = Math.Clamp(
            (int)Math.Floor(Math.Sqrt(PlacementEvidence.MaxFreeSpaceScanCells * terrainRatio)),
            1,
            terrainWidth);
        int searchHeight = Math.Clamp(
            PlacementEvidence.MaxFreeSpaceScanCells / searchWidth,
            1,
            terrainHeight);

        if (searchWidth * searchHeight > PlacementEvidence.MaxFreeSpaceScanCells)
            searchHeight = Math.Max(1, PlacementEvidence.MaxFreeSpaceScanCells / searchWidth);

        int x1 = Math.Clamp(center.X - searchWidth / 2, terrainBounds.X1, terrainBounds.X2 - searchWidth + 1);
        int z1 = Math.Clamp(center.Z - searchHeight / 2, terrainBounds.Z1, terrainBounds.Z2 - searchHeight + 1);
        notes.Add($"grow-zone search centered on Home anchor and capped at {searchWidth}x{searchHeight} cells by scan budget");
        return new MapRect(x1, z1, x1 + searchWidth - 1, z1 + searchHeight - 1);
    }

    private static HashSet<MapCell> BuildZoneBlockedMask(
        ZoneRequest request,
        TerrainSnapshot terrain,
        ColonyState colonyState,
        MapRect searchBounds)
    {
        HashSet<MapCell> cells = [];
        foreach (TerrainCellRecord cell in terrain.Cells.Where(cell => Inside(cell.X, cell.Z, searchBounds)))
        {
            if (!CellSupportsTerrainNeed(request, cell))
                cells.Add(new MapCell(cell.X, cell.Z));
        }

        foreach (MapZoneRecord zone in colonyState.Zones.Value.Zones.Where(zone => zone.IsGrowing))
            AddCells(cells, zone.Cells, searchBounds);
        foreach (StockpileZone stockpile in colonyState.Stockpiles.Value.Zones)
        {
            AddCells(cells, stockpile.Cells, searchBounds);
            AddCell(cells, stockpile.Center, searchBounds);
        }
        foreach (BuildingRecord building in colonyState.Buildings.Value.Buildings)
            AddCell(cells, building.Position, searchBounds);
        foreach (RoomRecord room in colonyState.Rooms.Value.Rooms)
            AddCells(cells, room.Cells, searchBounds);
        foreach (PlantRecord plant in colonyState.Plants.Value.Plants.Where(plant => plant.IsCrop))
            AddCell(cells, plant.Position, searchBounds);

        return cells;
    }

    private static void AddCells(HashSet<MapCell> cells, IReadOnlyList<MapPosition> positions, MapRect searchBounds)
    {
        foreach (MapPosition position in positions)
            AddCell(cells, position, searchBounds);
    }

    private static void AddCell(HashSet<MapCell> cells, MapPosition? position, MapRect searchBounds)
    {
        if (position is not null && Inside(position.X, position.Z, searchBounds))
            cells.Add(position.ToMapCell());
    }

    private static bool CellSupportsTerrainNeed(ZoneRequest request, TerrainCellRecord cell) =>
        request.Terrain?.MustSupportGrowing == false || cell.SupportsGrowing;

    private static IReadOnlyList<ResolvedAnchor> ResolveZoneAnchors(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState colonyState,
        SearchContext searchContext)
    {
        IReadOnlyList<AdjacencyHint> adjacency = request.Adjacency ?? [];
        if (!adjacency.Any(hint => hint.Relation == AdjacencyRelation.Near))
            adjacency = [new AdjacencyHint(AdjacencyRelation.Near, "storage")];

        List<ResolvedAnchor> anchors = AnchorResolver
            .ResolveNear(adjacency, briefing, colonyState.Stockpiles.Value.Zones)
            .OrderBy(anchor => anchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (anchors.Count == 0)
        {
            WillieRoomAnchor fallbackAnchor = searchContext.FallbackAnchor ?? BuildSearchBoundsAnchor(searchContext.Bounds);
            MapPosition fallback = fallbackAnchor.Centroid ?? CenterOf(fallbackAnchor.Bounds ?? searchContext.Bounds);
            anchors.Add(new ResolvedAnchor(
                fallbackAnchor,
                fallback,
                AnchorMatchReason.BuildableRegionFallback));
        }

        return anchors;
    }

    private static WillieRoomAnchor BuildSearchBoundsAnchor(MapRect searchBounds)
    {
        MapPosition fallback = CenterOf(searchBounds);
        return new WillieRoomAnchor(
            "search:bounds",
            RoomClass.BuildableRegion,
            "search bounds",
            searchBounds.Area,
            fallback,
            [])
        {
            Bounds = searchBounds
        };
    }

    private static IReadOnlyList<ZoneCandidate> BuildCandidatesFromFreeRects(
        ZoneRequest request,
        int mapId,
        TerrainIndex terrain,
        IReadOnlyList<FreeRect> freeRects,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        int targetCount = Math.Clamp(request.TileCount ?? DefaultTileCount, 1, MaxTileCount);
        IReadOnlyList<(int Width, int Height)> dimensions = CandidateSizesForTarget(targetCount);
        List<ZoneCandidate> candidates = [];

        foreach (FreeRect freeRect in freeRects)
        {
            foreach ((int width, int height) in dimensions)
            {
                RectSize size = new(width, height);
                if (!freeRect.CanFit(size))
                    continue;

                foreach (MapCell origin in CandidateOrigins(request, terrain, freeRect, size, anchors))
                {
                    MapRect rect = new(origin.X, origin.Z, origin.X + width - 1, origin.Z + height - 1);
                    ZoneCandidate candidate = BuildCandidate(
                        request,
                        mapId,
                        rect,
                        targetCount,
                        terrain,
                        anchors);
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.NearestAnchorDistance)
            .ThenBy(candidate => candidate.Rect.X1)
            .ThenBy(candidate => candidate.Rect.Z1)
            .ThenBy(candidate => candidate.Rect.Area)
            .ToList();
    }

    private static IReadOnlyList<MapCell> CandidateOrigins(
        ZoneRequest request,
        TerrainIndex terrain,
        FreeRect freeRect,
        RectSize size,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        List<MapCell> origins = [];
        MapCell? bestOrigin = BestOrigin(request, terrain, freeRect, size, anchors);
        if (bestOrigin is not null)
            origins.Add(bestOrigin);

        foreach (ResolvedAnchor anchor in anchors)
            origins.Add(PreferredOrigin(freeRect, size, anchor.TargetCell.ToMapCell()));

        origins.Add(new MapCell(freeRect.MinX, freeRect.MinZ));
        origins.Add(new MapCell(freeRect.MaxXExclusive - size.Width, freeRect.MinZ));
        origins.Add(new MapCell(freeRect.MinX, freeRect.MaxZExclusive - size.Height));
        origins.Add(new MapCell(freeRect.MaxXExclusive - size.Width, freeRect.MaxZExclusive - size.Height));

        return origins
            .Distinct()
            .Where(origin => OriginFits(freeRect, size, origin))
            .ToList();
    }

    private static MapCell? BestOrigin(
        ZoneRequest request,
        TerrainIndex terrain,
        FreeRect freeRect,
        RectSize size,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        int maxX = freeRect.MaxXExclusive - size.Width;
        int maxZ = freeRect.MaxZExclusive - size.Height;
        MapCell? bestOrigin = null;
        double bestScore = double.NegativeInfinity;
        int bestDistance = int.MaxValue;

        for (int z = freeRect.MinZ; z <= maxZ; z++)
        {
            for (int x = freeRect.MinX; x <= maxX; x++)
            {
                MapRect rect = new(x, z, x + size.Width - 1, z + size.Height - 1);
                int nearestDistance = NearestAnchorDistance(anchors, rect);
                double score = ScoreFor(
                    request,
                    rect,
                    terrain.AverageFertility(rect),
                    nearestDistance,
                    terrain.PreferredTerrainFraction(rect));
                if (score > bestScore ||
                    (score == bestScore && nearestDistance < bestDistance) ||
                    (score == bestScore && nearestDistance == bestDistance && bestOrigin is not null && z < bestOrigin.Z) ||
                    (score == bestScore && nearestDistance == bestDistance && bestOrigin is not null && z == bestOrigin.Z && x < bestOrigin.X))
                {
                    bestScore = score;
                    bestDistance = nearestDistance;
                    bestOrigin = new MapCell(x, z);
                }
            }
        }

        return bestOrigin;
    }

    private static MapCell PreferredOrigin(FreeRect freeRect, RectSize size, MapCell target)
    {
        int maxX = freeRect.MaxXExclusive - size.Width;
        int maxZ = freeRect.MaxZExclusive - size.Height;
        int preferredX = Math.Clamp(target.X - size.Width / 2, freeRect.MinX, maxX);
        int preferredZ = Math.Clamp(target.Z - size.Height / 2, freeRect.MinZ, maxZ);
        return new MapCell(preferredX, preferredZ);
    }

    private static bool OriginFits(FreeRect freeRect, RectSize size, MapCell origin) =>
        origin.X >= freeRect.MinX &&
        origin.Z >= freeRect.MinZ &&
        origin.X + size.Width <= freeRect.MaxXExclusive &&
        origin.Z + size.Height <= freeRect.MaxZExclusive;

    private static ZoneCandidate BuildCandidate(
        ZoneRequest request,
        int mapId,
        MapRect rect,
        int targetCount,
        TerrainIndex terrain,
        IReadOnlyList<ResolvedAnchor> anchors)
    {
        IReadOnlyList<TerrainCellRecord> cells = terrain.Cells(rect);
        double averageFertility = cells.Average(cell => cell.Fertility);
        ResolvedAnchor nearestAnchor = anchors
            .OrderBy(anchor => DistanceToRect(anchor.TargetCell, rect))
            .ThenBy(anchor => anchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .First();
        int nearestDistance = DistanceToRect(nearestAnchor.TargetCell, rect);
        double preferredTerrainFraction = terrain.PreferredTerrainFraction(rect);
        IReadOnlyList<MetricValue> metrics = MetricsFor(
            request,
            rect,
            averageFertility,
            nearestDistance,
            preferredTerrainFraction);
        double score = metrics.Sum(metric => metric.Contribution);
        return new ZoneCandidate(
            MapId: mapId,
            Rect: rect,
            Cells: cells,
            PlantDef: request.PlantDef ?? "Plant_Rice",
            TargetCount: targetCount,
            AverageFertility: averageFertility,
            NearestAnchor: nearestAnchor,
            NearestAnchorDistance: nearestDistance,
            Metrics: metrics,
            Score: score);
    }

    private static int NearestAnchorDistance(IReadOnlyList<ResolvedAnchor> anchors, MapRect rect) =>
        anchors.Min(anchor => DistanceToRect(anchor.TargetCell, rect));

    private static double ScoreFor(
        ZoneRequest request,
        MapRect rect,
        double averageFertility,
        int nearestDistance,
        double preferredTerrainFraction)
    {
        double score = Math.Clamp(averageFertility / 1.4d, 0d, 1d) * 6d;
        double preferredFertility = request.Terrain?.PreferredFertility ?? 1.0d;
        score += Math.Clamp(averageFertility / Math.Max(0.01d, preferredFertility), 0d, 1d) * 2d;
        int width = rect.X2 - rect.X1 + 1;
        int height = rect.Z2 - rect.Z1 + 1;
        score += (1d / (1d + Math.Abs(width - height))) * 2d;
        score += (1d / (1d + nearestDistance)) * 3d;
        if (request.Terrain?.PreferredTerrainDefs is { Count: > 0 })
            score += preferredTerrainFraction;

        return score;
    }

    private static IReadOnlyList<MetricValue> MetricsFor(
        ZoneRequest request,
        MapRect rect,
        double averageFertility,
        int nearestDistance,
        double preferredTerrainFraction)
    {
        List<MetricValue> metrics = [];
        AddMetric(metrics, "average_fertility", averageFertility, "fertility", Math.Clamp(averageFertility / 1.4d, 0d, 1d), 6d, "higher");
        double preferredFertility = request.Terrain?.PreferredFertility ?? 1.0d;
        AddMetric(metrics, "preferred_fertility", averageFertility, "fertility", Math.Clamp(averageFertility / Math.Max(0.01d, preferredFertility), 0d, 1d), 2d, "higher");
        int width = rect.X2 - rect.X1 + 1;
        int height = rect.Z2 - rect.Z1 + 1;
        AddMetric(metrics, "compactness", Math.Abs(width - height), "shape_delta", 1d / (1d + Math.Abs(width - height)), 2d, "lower");
        AddMetric(metrics, "anchor_distance", nearestDistance, "cells", 1d / (1d + nearestDistance), 3d, "lower");

        if (request.Terrain?.PreferredTerrainDefs is { Count: > 0 })
        {
            AddMetric(metrics, "preferred_terrain_defs", preferredTerrainFraction, "fraction", preferredTerrainFraction, 1d, "higher");
        }

        return metrics;
    }

    private static void AddMetric(
        List<MetricValue> metrics,
        string id,
        double rawValue,
        string unit,
        double normalized,
        double weight,
        string better)
    {
        metrics.Add(new MetricValue(
            Id: id,
            RawValue: rawValue,
            Unit: unit,
            Normalized: normalized,
            Weight: weight,
            Contribution: normalized * weight,
            Better: better));
    }

    private static IReadOnlyList<ZoneCandidate> SelectOptions(IReadOnlyList<ZoneCandidate> candidates)
    {
        List<ZoneCandidate> selected = [];
        foreach (ZoneCandidate candidate in candidates)
        {
            if (selected.Any(existing => Overlaps(existing.Rect, candidate.Rect)))
                continue;

            selected.Add(candidate);
            if (selected.Count == MaxOptionCount)
                break;
        }

        return selected;
    }

    private static AdviceOption AssembleOption(ZoneRequest request, ZoneCandidate candidate)
    {
        string plantDef = request.PlantDef ?? candidate.PlantDef;
        IReadOnlyList<BlueprintAsset> assets = candidate.Cells
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Z)
            .Select(cell => new BlueprintAsset(
                Role: "zone_cell",
                DefName: plantDef,
                StuffDefName: null,
                Cell: new MapCell(cell.X, cell.Z),
                Rotation: 0))
            .ToList();
        string label = $"Growing zone {candidate.Rect.X1},{candidate.Rect.Z1}";
        return new AdviceOption(
            Id: $"zone_growing_{SanitizeId(plantDef)}_{candidate.Rect.X1}_{candidate.Rect.Z1}_{candidate.Rect.Area}",
            Label: label,
            Summary: $"{candidate.Rect.Area}-tile {plantDef} growing zone at {FormatRect(candidate.Rect)}.",
            BlueprintGroup: new BlueprintGroup(label, candidate.MapId, assets),
            EstimatedMaterials: [],
            TradeoffNote: $"{FormatNumber(candidate.AverageFertility)} avg fertility; nearest {AnchorLabel(candidate.NearestAnchor)} {candidate.NearestAnchorDistance} cells; {candidate.Rect.X2 - candidate.Rect.X1 + 1}x{candidate.Rect.Z2 - candidate.Rect.Z1 + 1} rectangle.");
    }

    private static PlacementDraftTrace TraceFor(ZoneCandidate candidate, string status, string? reason) =>
        new(
            GeneratorId: "grow_zone_rect",
            AnchorRoomId: candidate.NearestAnchor.Anchor.RoomId,
            Status: status,
            Reason: reason,
            Metrics: candidate.Metrics);

    private static PlacementResult NoFit(
        NoFitReason reason,
        IReadOnlyList<PlacementDraftTrace> traces,
        IReadOnlyList<string> notes,
        string note)
    {
        List<string> mergedNotes = [.. notes, note];
        return new PlacementResult(
            Options: [],
            Trace: new PlacementTrace("grow_zone_placement_solver", traces, mergedNotes),
            NoFit: reason,
            Draftable: reason is NoFitReason.NoTerrainGrid or NoFitReason.NoGrowableCells or NoFitReason.UnsupportedZoneClass
                ? PlacementReadiness.Blocked
                : PlacementReadiness.Ready,
            PlacementValid: PlacementReadiness.Blocked,
            MaterialsReady: PlacementReadiness.Ready,
            ApplyReady: PlacementReadiness.Blocked);
    }

    private static IReadOnlyList<(int Width, int Height)> CandidateSizesForTarget(int targetCount)
    {
        List<(int Width, int Height)> dimensions = [];
        int maxArea = targetCount + Math.Max(4, targetCount / 6);
        for (int width = 1; width <= Math.Min(16, maxArea); width++)
        {
            int height = (int)Math.Ceiling(targetCount / (double)width);
            if (height > 16)
                continue;
            if (width * height > maxArea)
                continue;

            dimensions.Add((width, height));
            if (width != height)
                dimensions.Add((height, width));
        }

        return dimensions
            .Distinct()
            .OrderBy(pair => Math.Abs(pair.Width - pair.Height))
            .ThenBy(pair => pair.Width * pair.Height)
            .ThenBy(pair => pair.Width)
            .ToList();
    }

    private static int DistanceToRect(MapPosition position, MapRect rect)
    {
        int dx = position.X < rect.X1
            ? rect.X1 - position.X
            : position.X > rect.X2
                ? position.X - rect.X2
                : 0;
        int dz = position.Z < rect.Z1
            ? rect.Z1 - position.Z
            : position.Z > rect.Z2
                ? position.Z - rect.Z2
                : 0;
        return dx + dz;
    }

    private static bool Inside(int x, int z, MapRect rect) =>
        x >= rect.X1 && x <= rect.X2 && z >= rect.Z1 && z <= rect.Z2;

    private static bool Overlaps(MapRect a, MapRect b) =>
        a.X1 <= b.X2 && a.X2 >= b.X1 && a.Z1 <= b.Z2 && a.Z2 >= b.Z1;

    private static MapRect Clamp(MapRect rect, MapRect bounds) =>
        new(
            X1: Math.Clamp(rect.X1, bounds.X1, bounds.X2),
            Z1: Math.Clamp(rect.Z1, bounds.Z1, bounds.Z2),
            X2: Math.Clamp(rect.X2, bounds.X1, bounds.X2),
            Z2: Math.Clamp(rect.Z2, bounds.Z1, bounds.Z2));

    private static MapPosition CenterOf(MapRect bounds) =>
        new(
            X: (int)Math.Round((bounds.X1 + bounds.X2) / 2d),
            Y: 0,
            Z: (int)Math.Round((bounds.Z1 + bounds.Z2) / 2d));

    private static string SanitizeId(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray());

    private static string FormatRect(MapRect rect) =>
        $"{rect.X1},{rect.Z1}-{rect.X2},{rect.Z2}";

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string AnchorLabel(ResolvedAnchor anchor)
    {
        if (anchor.Anchor.Class == RoomClass.BuildableRegion)
            return anchor.Anchor.RoleLabel;

        if (anchor.Anchor.RoomId.StartsWith("stockpile:", StringComparison.OrdinalIgnoreCase))
            return "storage";

        return anchor.Anchor.Class.ToString().ToLowerInvariant();
    }

    private sealed class TerrainIndex
    {
        private readonly int width;
        private readonly int height;
        private readonly TerrainCellRecord?[,] cells;
        private readonly double[,] fertilityPrefix;
        private readonly int[,] preferredTerrainPrefix;
        private readonly bool hasPreferredTerrainDefs;

        private TerrainIndex(
            int width,
            int height,
            TerrainCellRecord?[,] cells,
            double[,] fertilityPrefix,
            int[,] preferredTerrainPrefix,
            bool hasPreferredTerrainDefs)
        {
            this.width = width;
            this.height = height;
            this.cells = cells;
            this.fertilityPrefix = fertilityPrefix;
            this.preferredTerrainPrefix = preferredTerrainPrefix;
            this.hasPreferredTerrainDefs = hasPreferredTerrainDefs;
        }

        public static TerrainIndex Build(TerrainSnapshot terrain, ZoneRequest request)
        {
            TerrainCellRecord?[,] cells = new TerrainCellRecord?[terrain.Width, terrain.Height];
            double[,] fertility = new double[terrain.Width, terrain.Height];
            int[,] preferredTerrain = new int[terrain.Width, terrain.Height];
            HashSet<string> preferredDefs = request.Terrain?.PreferredTerrainDefs is { Count: > 0 } defs
                ? defs.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];

            foreach (TerrainCellRecord cell in terrain.Cells)
            {
                if (cell.X < 0 || cell.Z < 0 || cell.X >= terrain.Width || cell.Z >= terrain.Height)
                    continue;

                cells[cell.X, cell.Z] = cell;
                fertility[cell.X, cell.Z] = cell.Fertility;
                preferredTerrain[cell.X, cell.Z] = preferredDefs.Contains(cell.TerrainDef) ? 1 : 0;
            }

            return new TerrainIndex(
                terrain.Width,
                terrain.Height,
                cells,
                BuildDoublePrefix(fertility, terrain.Width, terrain.Height),
                BuildIntPrefix(preferredTerrain, terrain.Width, terrain.Height),
                preferredDefs.Count > 0);
        }

        public double AverageFertility(MapRect rect) =>
            Sum(fertilityPrefix, rect) / Math.Max(1, rect.Area);

        public double PreferredTerrainFraction(MapRect rect) =>
            hasPreferredTerrainDefs
                ? Sum(preferredTerrainPrefix, rect) / (double)Math.Max(1, rect.Area)
                : 0d;

        public IReadOnlyList<TerrainCellRecord> Cells(MapRect rect)
        {
            List<TerrainCellRecord> result = [];
            for (int z = rect.Z1; z <= rect.Z2; z++)
            {
                for (int x = rect.X1; x <= rect.X2; x++)
                {
                    if (x < 0 || z < 0 || x >= width || z >= height)
                        continue;

                    if (cells[x, z] is TerrainCellRecord cell)
                        result.Add(cell);
                }
            }

            return result;
        }

        private static double[,] BuildDoublePrefix(double[,] values, int width, int height)
        {
            double[,] prefix = new double[width + 1, height + 1];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    prefix[x + 1, z + 1] =
                        values[x, z] +
                        prefix[x, z + 1] +
                        prefix[x + 1, z] -
                        prefix[x, z];
                }
            }

            return prefix;
        }

        private static int[,] BuildIntPrefix(int[,] values, int width, int height)
        {
            int[,] prefix = new int[width + 1, height + 1];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    prefix[x + 1, z + 1] =
                        values[x, z] +
                        prefix[x, z + 1] +
                        prefix[x + 1, z] -
                        prefix[x, z];
                }
            }

            return prefix;
        }

        private static double Sum(double[,] prefix, MapRect rect) =>
            prefix[rect.X2 + 1, rect.Z2 + 1] -
            prefix[rect.X1, rect.Z2 + 1] -
            prefix[rect.X2 + 1, rect.Z1] +
            prefix[rect.X1, rect.Z1];

        private static int Sum(int[,] prefix, MapRect rect) =>
            prefix[rect.X2 + 1, rect.Z2 + 1] -
            prefix[rect.X1, rect.Z2 + 1] -
            prefix[rect.X2 + 1, rect.Z1] +
            prefix[rect.X1, rect.Z1];
    }

    private sealed record ZoneCandidate(
        int MapId,
        MapRect Rect,
        IReadOnlyList<TerrainCellRecord> Cells,
        string PlantDef,
        int TargetCount,
        double AverageFertility,
        ResolvedAnchor NearestAnchor,
        int NearestAnchorDistance,
        IReadOnlyList<MetricValue> Metrics,
        double Score);

    private sealed record SearchContext(
        MapRect Bounds,
        WillieRoomAnchor? FallbackAnchor);
}
