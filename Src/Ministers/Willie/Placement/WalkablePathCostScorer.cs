using RimBob.Core.Advice;
using RimBob.Core.Placement;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class WalkablePathCostScorer : IPlacementScorer
{
    private readonly ScoreWeights weights;

    public WalkablePathCostScorer(ScoreWeights? weights = null)
    {
        this.weights = weights ?? ScoreWeights.Default;
    }

    public string Id => "walkable_path_cost";

    public IReadOnlyList<ScoredDraft> Score(
        IReadOnlyList<PlacementDraft> gatedDrafts,
        PathCostLookup pathCosts,
        PlacementEvidence evidence)
    {
        List<ScoredDraft> scored = [];
        for (int draftIndex = 0; draftIndex < gatedDrafts.Count; draftIndex++)
        {
            PlacementDraft draft = gatedDrafts[draftIndex];
            ScoredDraft? scoredDraft = pathCosts.ProbeAvailable
                ? ScoreWithProbeResult(draft, pathCosts.EntriesForDraft(draftIndex), evidence)
                : ScoreWithManhattanFallback(draft, evidence);

            if (scoredDraft is not null)
                scored.Add(scoredDraft);
        }

        return scored;
    }

    private ScoredDraft? ScoreWithProbeResult(
        PlacementDraft draft,
        IReadOnlyList<PathCostLookupEntry> entries,
        PlacementEvidence evidence)
    {
        PathCostResult? reachable = entries
            .Select(entry => entry.Result)
            .Where(result => result is { Reachable: true })
            .OrderBy(result => result!.Cost)
            .FirstOrDefault();
        return reachable is null
            ? null
            : ScoreDraft(draft, reachable.Cost, "path_tiles", evidence);
    }

    private ScoredDraft? ScoreWithManhattanFallback(PlacementDraft draft, PlacementEvidence evidence)
    {
        if (draft.AccessCells.Count == 0)
            return null;

        int fallbackDistance = draft.AccessCells
            .Select(cell => MapDistance.Manhattan(cell.ToMapPosition(), draft.SourceAnchor.TargetCell))
            .Min();
        return ScoreDraft(draft, fallbackDistance, "tiles", evidence);
    }

    private ScoredDraft ScoreDraft(
        PlacementDraft draft,
        int rawCost,
        string unit,
        PlacementEvidence evidence)
    {
        double normalized = 1d / (1d + rawCost);
        MetricValue distance = new(
            Id: "freezer_to_kitchen_distance",
            RawValue: rawCost,
            Unit: unit,
            Normalized: normalized,
            Weight: weights.WalkablePathCost,
            Contribution: normalized * weights.WalkablePathCost,
            Better: "lower");
        int expansionTiles = evidence.CountFreeExpansionTilesAround(draft.Group.Assets);
        double expansionNormalized = Math.Min(1d, expansionTiles / 32d);
        MetricValue expansionRoom = new(
            Id: "expansion_room",
            RawValue: expansionTiles,
            Unit: "free_tiles",
            Normalized: expansionNormalized,
            Weight: weights.ExpansionRoom,
            Contribution: expansionNormalized * weights.ExpansionRoom,
            Better: "higher");
        double generatorConfidenceValue = GeneratorConfidenceFor(draft.GeneratorId);
        MetricValue generatorConfidence = new(
            Id: "generator_confidence",
            RawValue: generatorConfidenceValue,
            Unit: null,
            Normalized: generatorConfidenceValue,
            Weight: weights.GeneratorConfidence,
            Contribution: generatorConfidenceValue * weights.GeneratorConfidence,
            Better: "higher");
        MetricValue buildOrderSafety = BuildOrderSafetyMetric(draft, evidence);
        return new ScoredDraft(draft, rawCost, [distance, expansionRoom, generatorConfidence, buildOrderSafety]);
    }

    private static double GeneratorConfidenceFor(string generatorId) =>
        string.Equals(generatorId, "template_anchored", StringComparison.OrdinalIgnoreCase)
            ? 0.8
            : string.Equals(generatorId, "largest_empty_rect", StringComparison.OrdinalIgnoreCase)
                ? 0.65
            : 0.5;

    private MetricValue BuildOrderSafetyMetric(PlacementDraft draft, PlacementEvidence evidence)
    {
        double riskPoints = 0;
        Footprint footprint = Footprint.From(draft.Group.Assets);
        if (evidence.Bounds is not null &&
            (footprint.MinX <= 0 ||
             footprint.MinZ <= 0 ||
             footprint.MaxX >= evidence.Bounds.Width - 1 ||
             footprint.MaxZ >= evidence.Bounds.Height - 1))
        {
            riskPoints += 3;
        }

        foreach (MapCell accessCell in draft.AccessCells)
        {
            riskPoints += OccupiedNeighborCount(accessCell, evidence) * 0.5;
        }

        double normalized = 1d - Math.Min(1d, riskPoints / 6d);
        return new MetricValue(
            Id: "build_order_safety",
            RawValue: normalized * 100d,
            Unit: "percent",
            Normalized: normalized,
            Weight: weights.BuildOrderSafety,
            Contribution: normalized * weights.BuildOrderSafety,
            Better: "higher");
    }

    private static int OccupiedNeighborCount(MapCell cell, PlacementEvidence evidence)
    {
        int occupiedNeighbors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;

                MapCell neighbor = new(cell.X + dx, cell.Z + dz);
                if (evidence.InBounds(neighbor) && evidence.IsOccupied(neighbor))
                    occupiedNeighbors++;
            }
        }

        return occupiedNeighbors;
    }
}
