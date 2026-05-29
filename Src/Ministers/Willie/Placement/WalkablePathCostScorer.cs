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
        PathCostLookup pathCosts)
    {
        List<ScoredDraft> scored = [];
        for (int draftIndex = 0; draftIndex < gatedDrafts.Count; draftIndex++)
        {
            PlacementDraft draft = gatedDrafts[draftIndex];
            ScoredDraft? scoredDraft = pathCosts.ProbeAvailable
                ? ScoreWithProbeResult(draft, pathCosts.EntriesForDraft(draftIndex))
                : ScoreWithManhattanFallback(draft);

            if (scoredDraft is not null)
                scored.Add(scoredDraft);
        }

        return scored;
    }

    private ScoredDraft? ScoreWithProbeResult(
        PlacementDraft draft,
        IReadOnlyList<PathCostLookupEntry> entries)
    {
        PathCostResult? reachable = entries
            .Select(entry => entry.Result)
            .Where(result => result is { Reachable: true })
            .OrderBy(result => result!.Cost)
            .FirstOrDefault();
        return reachable is null
            ? null
            : ScoreDraft(draft, reachable.Cost, "path_tiles");
    }

    private ScoredDraft ScoreWithManhattanFallback(PlacementDraft draft)
    {
        int fallbackDistance = draft.AccessCells
            .Select(cell => MapDistance.Manhattan(cell.ToMapPosition(), draft.SourceAnchor.TargetCell))
            .Min();
        return ScoreDraft(draft, fallbackDistance, "tiles");
    }

    private ScoredDraft ScoreDraft(PlacementDraft draft, int rawCost, string unit)
    {
        double normalized = 1d / (1d + rawCost);
        MetricValue metric = new(
            Id: "freezer_to_kitchen_distance",
            RawValue: rawCost,
            Unit: unit,
            Normalized: normalized,
            Weight: weights.WalkablePathCost,
            Contribution: normalized * weights.WalkablePathCost,
            Better: "lower");
        return new ScoredDraft(draft, rawCost, [metric]);
    }
}
