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

        MapRect searchBounds = SearchBounds(terrain, colonyState.Areas.Value, notes);
        HashSet<(int X, int Z)> blockedCells = BlockedCells(colonyState);
        List<ZoneAnchor> anchors = ZoneAnchors(request, briefing, colonyState, searchBounds);
        IReadOnlyList<ZoneCandidate> candidates = EnumerateCandidates(
            request,
            briefing.MapId,
            terrain,
            searchBounds,
            blockedCells,
            anchors);

        if (candidates.Count == 0)
        {
            bool anyGrowable = terrain.Cells.Any(cell =>
                cell.SupportsGrowing &&
                Inside(cell.X, cell.Z, searchBounds));
            bool anyUnblockedGrowable = terrain.Cells.Any(cell =>
                cell.SupportsGrowing &&
                Inside(cell.X, cell.Z, searchBounds) &&
                !blockedCells.Contains((cell.X, cell.Z)));
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
            ApplyReady: PlacementReadiness.Blocked));
    }

    private static MapRect SearchBounds(
        TerrainSnapshot terrain,
        MapAreaRegistry areas,
        List<string> notes)
    {
        MapArea? home = areas.Areas
            .Where(area => string.Equals(area.Type, "Area_Home", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(area.Type, "Home", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(area => area.CellCount)
            .ThenBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (home?.Bounds is not null)
        {
            if (home.Cells.Count == 0)
                notes.Add("Home area row did not include cells; using Home bounds as the grow-zone search region");

            return Clamp(home.Bounds, terrain);
        }

        notes.Add("no Home area bounds available; searching the full terrain grid");
        return new MapRect(0, 0, terrain.Width - 1, terrain.Height - 1);
    }

    private static HashSet<(int X, int Z)> BlockedCells(ColonyState colonyState)
    {
        HashSet<(int X, int Z)> cells = [];
        foreach (MapZoneRecord zone in colonyState.Zones.Value.Zones.Where(zone => zone.IsGrowing))
            AddCells(cells, zone.Cells);
        foreach (StockpileZone stockpile in colonyState.Stockpiles.Value.Zones)
        {
            AddCells(cells, stockpile.Cells);
            AddCell(cells, stockpile.Center);
        }
        foreach (BuildingRecord building in colonyState.Buildings.Value.Buildings)
            AddCell(cells, building.Position);
        foreach (RoomRecord room in colonyState.Rooms.Value.Rooms)
            AddCells(cells, room.Cells);
        foreach (PlantRecord plant in colonyState.Plants.Value.Plants.Where(plant => plant.IsCrop))
            AddCell(cells, plant.Position);

        return cells;
    }

    private static void AddCells(HashSet<(int X, int Z)> cells, IReadOnlyList<MapPosition> positions)
    {
        foreach (MapPosition position in positions)
            cells.Add((position.X, position.Z));
    }

    private static void AddCell(HashSet<(int X, int Z)> cells, MapPosition? position)
    {
        if (position is not null)
            cells.Add((position.X, position.Z));
    }

    private static List<ZoneAnchor> ZoneAnchors(
        ZoneRequest request,
        WillieBriefing briefing,
        ColonyState colonyState,
        MapRect searchBounds)
    {
        List<ZoneAnchor> anchors = [];
        IReadOnlyList<string> requestedTargets = (request.Adjacency ?? [])
            .Where(adjacency => adjacency.Relation == AdjacencyRelation.Near)
            .Select(adjacency => adjacency.Target)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool wantsStorage = requestedTargets.Count == 0 ||
            requestedTargets.Any(target => target.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("stockpile", StringComparison.OrdinalIgnoreCase));
        bool wantsKitchen = requestedTargets.Any(target => target.Contains("kitchen", StringComparison.OrdinalIgnoreCase));

        if (wantsStorage)
        {
            foreach (StockpileZone stockpile in colonyState.Stockpiles.Value.Zones.Where(zone => zone.Center is not null))
                anchors.Add(new ZoneAnchor($"stockpile:{stockpile.Id}", "storage", stockpile.Center!));
        }

        if (wantsKitchen)
        {
            foreach (WillieRoomAnchor anchor in briefing.AnchorInventory.Anchors
                         .Where(anchor => anchor.Class == RoomClass.Kitchen && anchor.Centroid is not null))
                anchors.Add(new ZoneAnchor($"room:{anchor.RoomId}", "kitchen", anchor.Centroid!));
        }

        if (anchors.Count == 0)
        {
            MapPosition fallback = new(
                X: (searchBounds.X1 + searchBounds.X2) / 2,
                Y: 0,
                Z: (searchBounds.Z1 + searchBounds.Z2) / 2);
            anchors.Add(new ZoneAnchor("home:bounds", "Home area", fallback));
        }

        return anchors
            .OrderBy(anchor => anchor.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ZoneCandidate> EnumerateCandidates(
        ZoneRequest request,
        int mapId,
        TerrainSnapshot terrain,
        MapRect searchBounds,
        HashSet<(int X, int Z)> blockedCells,
        IReadOnlyList<ZoneAnchor> anchors)
    {
        int targetCount = Math.Clamp(request.TileCount ?? DefaultTileCount, 1, MaxTileCount);
        IReadOnlyList<(int Width, int Height)> dimensions = CandidateDimensions(targetCount);
        Dictionary<(int X, int Z), TerrainCellRecord> cellByCoordinate = terrain.Cells
            .ToDictionary(cell => (cell.X, cell.Z), cell => cell);
        List<ZoneCandidate> candidates = [];

        foreach ((int width, int height) in dimensions)
        {
            int maxX = searchBounds.X2 - width + 1;
            int maxZ = searchBounds.Z2 - height + 1;
            for (int z = searchBounds.Z1; z <= maxZ; z++)
            {
                for (int x = searchBounds.X1; x <= maxX; x++)
                {
                    MapRect rect = new(x, z, x + width - 1, z + height - 1);
                    ZoneCandidate? candidate = TryBuildCandidate(
                        request,
                        mapId,
                        rect,
                        targetCount,
                        cellByCoordinate,
                        blockedCells,
                        anchors);
                    if (candidate is not null)
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

    private static ZoneCandidate? TryBuildCandidate(
        ZoneRequest request,
        int mapId,
        MapRect rect,
        int targetCount,
        IReadOnlyDictionary<(int X, int Z), TerrainCellRecord> cellByCoordinate,
        HashSet<(int X, int Z)> blockedCells,
        IReadOnlyList<ZoneAnchor> anchors)
    {
        List<TerrainCellRecord> cells = [];
        for (int z = rect.Z1; z <= rect.Z2; z++)
        {
            for (int x = rect.X1; x <= rect.X2; x++)
            {
                if (blockedCells.Contains((x, z)))
                    return null;
                if (!cellByCoordinate.TryGetValue((x, z), out TerrainCellRecord? cell))
                    return null;
                if (request.Terrain?.MustSupportGrowing != false && !cell.SupportsGrowing)
                    return null;

                cells.Add(cell);
            }
        }

        if (cells.Count < targetCount)
            return null;

        double averageFertility = cells.Average(cell => cell.Fertility);
        ZoneAnchor nearestAnchor = anchors
            .OrderBy(anchor => DistanceToRect(anchor.Position, rect))
            .ThenBy(anchor => anchor.Id, StringComparer.OrdinalIgnoreCase)
            .First();
        int nearestDistance = DistanceToRect(nearestAnchor.Position, rect);
        IReadOnlyList<MetricValue> metrics = MetricsFor(
            request,
            rect,
            cells,
            averageFertility,
            nearestDistance);
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

    private static IReadOnlyList<MetricValue> MetricsFor(
        ZoneRequest request,
        MapRect rect,
        IReadOnlyList<TerrainCellRecord> cells,
        double averageFertility,
        int nearestDistance)
    {
        List<MetricValue> metrics = [];
        AddMetric(metrics, "average_fertility", averageFertility, "fertility", Math.Clamp(averageFertility / 1.4d, 0d, 1d), 6d, "higher");
        double preferredFertility = request.Terrain?.PreferredFertility ?? 1.0d;
        AddMetric(metrics, "preferred_fertility", averageFertility, "fertility", Math.Clamp(averageFertility / Math.Max(0.01d, preferredFertility), 0d, 1d), 2d, "higher");
        int width = rect.X2 - rect.X1 + 1;
        int height = rect.Z2 - rect.Z1 + 1;
        AddMetric(metrics, "compactness", Math.Abs(width - height), "shape_delta", 1d / (1d + Math.Abs(width - height)), 2d, "lower");
        AddMetric(metrics, "anchor_distance", nearestDistance, "cells", 1d / (1d + nearestDistance), 3d, "lower");

        if (request.Terrain?.PreferredTerrainDefs is { Count: > 0 } preferredDefs)
        {
            HashSet<string> preferred = preferredDefs.ToHashSet(StringComparer.OrdinalIgnoreCase);
            double fraction = cells.Count(cell => preferred.Contains(cell.TerrainDef)) / (double)cells.Count;
            AddMetric(metrics, "preferred_terrain_defs", fraction, "fraction", fraction, 1d, "higher");
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

        return selected.Count > 0 ? selected : candidates.Take(MaxOptionCount).ToList();
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
            TradeoffNote: $"{FormatNumber(candidate.AverageFertility)} avg fertility; nearest {candidate.NearestAnchor.Label} {candidate.NearestAnchorDistance} cells; {candidate.Rect.X2 - candidate.Rect.X1 + 1}x{candidate.Rect.Z2 - candidate.Rect.Z1 + 1} rectangle.");
    }

    private static PlacementDraftTrace TraceFor(ZoneCandidate candidate, string status, string? reason) =>
        new(
            GeneratorId: "grow_zone_rect",
            AnchorRoomId: candidate.NearestAnchor.Id,
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

    private static IReadOnlyList<(int Width, int Height)> CandidateDimensions(int targetCount)
    {
        List<(int Width, int Height)> dimensions = [];
        int maxArea = targetCount + Math.Max(4, targetCount / 6);
        for (int width = 1; width <= Math.Min(16, maxArea); width++)
        {
            int height = (int)Math.Ceiling(targetCount / (double)width);
            if (width > 16 || height > 16)
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

    private static MapRect Clamp(MapRect rect, TerrainSnapshot terrain) =>
        new(
            X1: Math.Clamp(rect.X1, 0, Math.Max(0, terrain.Width - 1)),
            Z1: Math.Clamp(rect.Z1, 0, Math.Max(0, terrain.Height - 1)),
            X2: Math.Clamp(rect.X2, 0, Math.Max(0, terrain.Width - 1)),
            Z2: Math.Clamp(rect.Z2, 0, Math.Max(0, terrain.Height - 1)));

    private static string SanitizeId(string value) =>
        new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray());

    private static string FormatRect(MapRect rect) =>
        $"{rect.X1},{rect.Z1}-{rect.X2},{rect.Z2}";

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record ZoneAnchor(string Id, string Label, MapPosition Position);

    private sealed record ZoneCandidate(
        int MapId,
        MapRect Rect,
        IReadOnlyList<TerrainCellRecord> Cells,
        string PlantDef,
        int TargetCount,
        double AverageFertility,
        ZoneAnchor NearestAnchor,
        int NearestAnchorDistance,
        IReadOnlyList<MetricValue> Metrics,
        double Score);
}
