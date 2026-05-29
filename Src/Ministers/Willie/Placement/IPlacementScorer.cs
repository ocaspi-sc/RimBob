namespace RimBob.Ministers.Willie;

public interface IPlacementScorer
{
    string Id { get; }

    /// <summary>
    /// Deterministic contract: same gated drafts, lookup, and evidence produce the same ordered scored drafts.
    /// The solver's canonical final order is total score descending, raw cost, anchor room id, footprint origin, then generator id.
    /// </summary>
    IReadOnlyList<ScoredDraft> Score(
        IReadOnlyList<PlacementDraft> gatedDrafts,
        PathCostLookup pathCosts,
        PlacementEvidence evidence);
}

public sealed record ScoredDraft(
    PlacementDraft Draft,
    int RawCost,
    IReadOnlyList<MetricValue> Metrics,
    string? DiversityReason = null)
{
    public double TotalScore => Metrics.Sum(metric => metric.Contribution);
}
