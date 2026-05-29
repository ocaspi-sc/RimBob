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
        return new ScoredDraft(draft, rawCost, [distance, expansionRoom, generatorConfidence]);
    }

    private static double GeneratorConfidenceFor(string generatorId) =>
        string.Equals(generatorId, "template_anchored", StringComparison.OrdinalIgnoreCase)
            ? 0.8
            : string.Equals(generatorId, "largest_empty_rect", StringComparison.OrdinalIgnoreCase)
                ? 0.65
            : 0.5;
}
