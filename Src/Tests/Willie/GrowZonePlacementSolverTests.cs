using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Ministers.Willie;
using RimBob.State;

namespace RimBob.Tests.Willie;

public sealed class GrowZonePlacementSolverTests
{
    [Fact]
    public async Task SolveAsync_PrefersHighFertilityGrowableRectangle()
    {
        ColonyState state = StateWithTerrain(8, 8, (x, z) => x is >= 4 and <= 5 && z is >= 1 and <= 2 ? "SoilRich" : "Soil");

        PlacementResult result = await new GrowZonePlacementSolver().SolveAsync(Request(4), Briefing(), state);

        result.Options.Should().NotBeEmpty();
        AdviceOption option = result.Options[0];
        option.BlueprintGroup.Assets.Should().OnlyContain(asset => asset.DefName == "Plant_Rice");
        option.BlueprintGroup.Assets.Select(asset => (asset.Cell.X, asset.Cell.Z))
            .Should().BeEquivalentTo([(4, 1), (5, 1), (4, 2), (5, 2)]);
        option.TradeoffNote.Should().Contain("1.4 avg fertility");
        result.ApplyReady.Should().Be(PlacementReadiness.Ready);
    }

    [Fact]
    public async Task SolveAsync_RejectsExistingGrowingZoneCells()
    {
        ColonyState state = StateWithTerrain(8, 8, (x, z) => x is >= 4 and <= 5 && z is >= 1 and <= 2 ? "SoilRich" : "Soil");
        state.Zones.Update(new MapZoneRegistry([
            new MapZoneRecord(
                Id: "grow-1",
                Type: "GrowingZone",
                Label: "rice",
                CellCount: 4,
                PlantDef: "Plant_Rice",
                Bounds: new MapRect(4, 1, 5, 2),
                Centroid: new MapPosition(5, 0, 2),
                Cells: [new MapPosition(4, 0, 1), new MapPosition(5, 0, 1), new MapPosition(4, 0, 2), new MapPosition(5, 0, 2)])
        ]));

        PlacementResult result = await new GrowZonePlacementSolver().SolveAsync(Request(4), Briefing(), state);

        result.Options.Should().NotBeEmpty();
        HashSet<(int X, int Z)> existingZoneCells = [(4, 1), (5, 1), (4, 2), (5, 2)];
        result.Options[0].BlueprintGroup.Assets
            .Select(asset => (asset.Cell.X, asset.Cell.Z))
            .Should().NotContain(cell => existingZoneCells.Contains(cell));
    }

    [Fact]
    public async Task SolveAsync_RejectsNonGrowableTerrainCells()
    {
        ColonyState state = StateWithTerrain(6, 6, (x, z) => x is >= 4 and <= 5 && z is >= 4 and <= 5 ? "Soil" : "Sand");

        PlacementResult result = await new GrowZonePlacementSolver().SolveAsync(Request(4), Briefing(), state);

        result.Options.Should().NotBeEmpty();
        result.Options[0].BlueprintGroup.Assets
            .Select(asset => (asset.Cell.X, asset.Cell.Z))
            .Should().BeEquivalentTo([(4, 4), (5, 4), (4, 5), (5, 5)]);
    }

    [Fact]
    public async Task SolveAsync_UsesHomeAsAnchorButSearchesOutsideHome()
    {
        ColonyState state = StateWithTerrain(8, 6, (x, z) => x is >= 4 and <= 5 && z is >= 1 and <= 2 ? "Soil" : "Sand");
        MapRect homeBounds = new(0, 0, 2, 2);
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "home",
                Type: "Area_Home",
                Label: "Home",
                CellCount: homeBounds.Area,
                Bounds: homeBounds,
                Centroid: new MapPosition(1, 0, 1),
                Cells: CellsIn(homeBounds))
        ]));
        state.Stockpiles.Update(new StockpileLedger(
            [],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)));

        PlacementResult result = await new GrowZonePlacementSolver().SolveAsync(Request(4), Briefing(), state);

        result.Options.Should().NotBeEmpty();
        result.Options[0].BlueprintGroup.Assets
            .Select(asset => (asset.Cell.X, asset.Cell.Z))
            .Should().BeEquivalentTo([(4, 1), (5, 1), (4, 2), (5, 2)]);
        result.Trace.Drafts.Should().Contain(draft => draft.AnchorRoomId == "area:home");
        result.NoFit.Should().BeNull();
    }

    [Fact]
    public async Task SolveAsync_WhenTerrainHasNoCoordinateGrid_ReturnsNoFit()
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(7, "8x8"));
        state.Terrain.Update(new TerrainSnapshot(
            Width: 8,
            Height: 8,
            CellCountsByDef: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Soil"] = 64
            },
            DefsByName: TerrainDefs()));

        PlacementResult result = await new GrowZonePlacementSolver().SolveAsync(Request(4), Briefing(), state);

        result.Options.Should().BeEmpty();
        result.NoFit.Should().Be(NoFitReason.NoTerrainGrid);
        result.Trace.Notes.Should().Contain(note => note.Contains("no coordinate-addressable cell grid"));
    }

    private static ZoneRequest Request(int tiles) =>
        new(
            Request: $"{tiles} rice growing tiles near storage",
            Reason: "food buffer is low",
            ZoneClass: ZoneClass.Growing,
            PlantDef: "Plant_Rice",
            TileCount: tiles,
            Adjacency: [new AdjacencyHint(AdjacencyRelation.Near, "storage")],
            Terrain: new TerrainNeed(MustSupportGrowing: true, PreferredFertility: 1.4f),
            Priority: Priority.High,
            RequestedFrom: "Willie");

    private static ColonyState StateWithTerrain(
        int width,
        int height,
        Func<int, int, string> terrainAt)
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(7, $"{width}x{height}"));
        state.Terrain.Update(Terrain(width, height, terrainAt));
        state.Areas.Update(new MapAreaRegistry([
            new MapArea(
                Id: "home",
                Type: "Area_Home",
                Label: "Home",
                CellCount: width * height,
                Bounds: new MapRect(0, 0, width - 1, height - 1),
                Centroid: new MapPosition(width / 2, 0, height / 2))
        ]));
        state.Stockpiles.Update(new StockpileLedger(
            [new StockpileZone("stockpile-1", "StockpileZone", "food", 1, new MapPosition(0, 0, 0))],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)));
        return state;
    }

    private static IReadOnlyList<MapPosition> CellsIn(MapRect rect)
    {
        List<MapPosition> cells = [];
        for (int z = rect.Z1; z <= rect.Z2; z++)
        {
            for (int x = rect.X1; x <= rect.X2; x++)
                cells.Add(new MapPosition(x, 0, z));
        }

        return cells;
    }

    private static TerrainSnapshot Terrain(
        int width,
        int height,
        Func<int, int, string> terrainAt)
    {
        List<TerrainCellRecord> cells = [];
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, TerrainDefRecord> defs = TerrainDefs();
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                string def = terrainAt(x, z);
                TerrainDefRecord record = defs[def];
                counts[def] = counts.TryGetValue(def, out int current) ? current + 1 : 1;
                cells.Add(new TerrainCellRecord(x, z, def, record.Fertility, record.SupportsGrowing));
            }
        }

        return new TerrainSnapshot(width, height, counts, defs, cells);
    }

    private static IReadOnlyDictionary<string, TerrainDefRecord> TerrainDefs() =>
        new Dictionary<string, TerrainDefRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["Soil"] = new("Soil", "soil", 1f, ["Walkable", "GrowSoil"]),
            ["SoilRich"] = new("SoilRich", "rich soil", 1.4f, ["Walkable", "GrowSoil"]),
            ["Sand"] = new("Sand", "sand", 0.1f, ["Walkable"])
        };

    private static WillieBriefing Briefing() =>
        new(
            BriefingVersion: 1,
            Date: GameTime.Create("5th of Aprimay, 5500, 14h", 300_000, 5500, "Aprimay", 5, 14),
            GameTick: 300_000,
            MapId: 7,
            ColonistCount: 3,
            PowerStability: new WilliePowerStabilitySummary(900, 650, 800, 1000, 250, 1, 2),
            ThermalControl: new WillieThermalControlSummary(1, 0, 0),
            FunctionalRooms: new WillieFunctionalRoomsSummary(new Dictionary<string, int>()),
            StoragePlacement: new WillieStoragePlacementSummary(1, 40),
            MaterialBottleneck: new WillieMaterialBottleneckSummary([], [], 0, 0),
            FireRisk: new WillieFireRiskSummary(0),
            StalledBuilds: new WillieStalledBuildsSummary([], 0, 0, 0, 0),
            BaseLayout: new WillieBaseLayoutSummary(3, 8),
            DataCoverage: new WillieDataCoverage(true, true, true, true, true, true, true, false));
}
