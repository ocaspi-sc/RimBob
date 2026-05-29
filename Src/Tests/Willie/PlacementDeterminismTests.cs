using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Placement;
using RimBob.Ministers.Willie;

namespace RimBob.Tests.Willie;

public sealed class PlacementDeterminismTests
{
    [Fact]
    public async Task SolveAsync_SameInputsProduceSameOptionAndTrace()
    {
        PlacementResult first = await Solver().SolveAsync(
            PlacementSolverTests.SpecWithMaterials(),
            PlacementSolverTests.Briefing(),
            PlacementSolverTests.State([]));
        PlacementResult second = await Solver().SolveAsync(
            PlacementSolverTests.SpecWithMaterials(),
            PlacementSolverTests.Briefing(),
            PlacementSolverTests.State([]));

        second.Options.Should().BeEquivalentTo(first.Options, options => options.WithStrictOrdering());
        second.Trace.Should().BeEquivalentTo(first.Trace, options => options.WithStrictOrdering());
        MetricValue metric = first.Trace.Drafts
            .Where(trace => trace.Status == "scored")
            .SelectMany(trace => trace.Metrics)
            .First(metric => metric.Id == "freezer_to_kitchen_distance");
        metric.RawValue.Should().Be(14);
        metric.Unit.Should().Be("path_tiles");
        metric.Normalized.Should().BeGreaterThan(0);
    }

    private static PlacementSolver Solver() =>
        new(new DeterministicPathCostProbe(), new DeterministicPlacementValidator());

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
