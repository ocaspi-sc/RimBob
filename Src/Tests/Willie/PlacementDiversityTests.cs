using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Placement;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class PlacementDiversityTests
{
    [Fact]
    public async Task SolveAsync_EmitsDistinctOptionsWithMetricBackedTradeoffNotes()
    {
        PlacementResult result = await Solver().SolveAsync(
            PlacementSolverTests.SpecWithMaterials(),
            PlacementSolverTests.Briefing(),
            PlacementSolverTests.State([]));

        result.NoFit.Should().BeNull();
        result.Options.Should().HaveCount(3);
        result.Options
            .Select(option => FootprintKey(option.BlueprintGroup.Assets))
            .Should()
            .OnlyHaveUniqueItems();
        result.Options
            .Select(option => option.TradeoffNote)
            .Should()
            .OnlyContain(note => !string.IsNullOrWhiteSpace(note));
        result.Options
            .Select(option => option.TradeoffNote)
            .Should()
            .OnlyHaveUniqueItems();

        HashSet<string> validatedMetricIds = result.Trace.Drafts
            .Where(trace => trace.Status == "validated")
            .SelectMany(trace => trace.Metrics)
            .Select(metric => metric.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (AdviceOption option in result.Options)
        {
            string note = option.TradeoffNote!;
            validatedMetricIds.Should().Contain(MetricIdForTradeoffNote(note));
            note.Should().Contain("footprint");
        }
    }

    [Fact]
    public async Task SolveAsync_SameInputsProduceSameOrderedOptionsAndTradeoffs()
    {
        PlacementResult first = await Solver().SolveAsync(
            PlacementSolverTests.SpecWithMaterials(),
            PlacementSolverTests.Briefing(),
            PlacementSolverTests.State([]));
        PlacementResult second = await Solver().SolveAsync(
            PlacementSolverTests.SpecWithMaterials(),
            PlacementSolverTests.Briefing(),
            PlacementSolverTests.State([]));

        second.Options
            .Select(option => option.Id)
            .Should()
            .Equal(first.Options.Select(option => option.Id));
        second.Options
            .Select(option => option.TradeoffNote)
            .Should()
            .Equal(first.Options.Select(option => option.TradeoffNote));
        second.Trace.Should().BeEquivalentTo(first.Trace, options => options.WithStrictOrdering());
    }

    private static PlacementSolver Solver() =>
        new(new DeterministicPathCostProbe(), new DeterministicPlacementValidator());

    private static string FootprintKey(IReadOnlyList<BlueprintAsset> assets)
    {
        Footprint footprint = Footprint.From(assets);
        return $"{footprint.MinX},{footprint.MinZ},{footprint.Width},{footprint.Height}";
    }

    private static string MetricIdForTradeoffNote(string note)
    {
        if (note.StartsWith("Closest to ", StringComparison.Ordinal))
            return "freezer_to_kitchen_distance";
        if (note.StartsWith("Most room to expand:", StringComparison.Ordinal))
            return "expansion_room";
        if (note.StartsWith("Cheapest materials:", StringComparison.Ordinal))
            return "material_cost";

        return "unknown";
    }

    private sealed class DeterministicPathCostProbe : IPathCostProbe
    {
        public Task<IReadOnlyList<PathCostResult>> GetPathCostsAsync(
            int mapId,
            IReadOnlyList<PathCostPair> pairs,
            string tier = "region",
            string mode = "pass_doors",
            string peMode = "on_cell",
            CancellationToken ct = default)
        {
            IReadOnlyList<PathCostResult> results = pairs
                .Select(pair => new PathCostResult(true, 14, pair.From, pair.To))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class DeterministicPlacementValidator : IPlacementValidator
    {
        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            PlacementValidationResult result = new(
                CanPlaceAll: true,
                Items: [],
                Cost: [new MaterialEstimate("BlocksGranite", 5)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }
}
