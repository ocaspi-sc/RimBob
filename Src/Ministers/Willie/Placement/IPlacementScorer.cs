namespace RimBob.Ministers.Willie;

public interface IPlacementScorer
{
    string Id { get; }

    /// <summary>
    /// Deterministic contract: same gated drafts and lookup produce the same ordered scored drafts.
    /// </summary>
    IReadOnlyList<ScoredDraft> Score(
        IReadOnlyList<PlacementDraft> gatedDrafts,
        PathCostLookup pathCosts);
}

public sealed record ScoredDraft(
    PlacementDraft Draft,
    int RawCost,
    IReadOnlyList<MetricValue> Metrics);
