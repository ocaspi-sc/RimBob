using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public static class DiverseSelector
{
    private const string DiversityMetricId = "diversity_bonus";

    public static IReadOnlyList<ScoredDraft> SelectTopK(
        IReadOnlyList<ScoredDraft> ranked,
        int maxValidate)
    {
        if (maxValidate <= 0 || ranked.Count == 0)
            return [];

        List<ScoredDraft> remaining = OrderByRank(ranked).ToList();
        List<ScoredDraft> selected = [];
        while (remaining.Count > 0 && selected.Count < maxValidate)
        {
            ScoredDraft next;
            if (selected.Count == 0)
            {
                string reason = remaining.Count == 1 ? "only_candidate" : "highest_score";
                next = WithDiversity(remaining[0], reason, normalizedBonus: 0);
            }
            else
            {
                next = remaining
                    .Select(candidate =>
                    {
                        DiversityScore diversity = ScoreDiversity(candidate, selected);
                        return WithDiversity(candidate, diversity.Reason, diversity.NormalizedBonus);
                    })
                    .OrderByDescending(candidate => candidate.TotalScore)
                    .ThenBy(candidate => candidate.RawCost)
                    .ThenBy(candidate => candidate.Draft.SourceAnchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => Footprint.From(candidate.Draft.Group.Assets).MinX)
                    .ThenBy(candidate => Footprint.From(candidate.Draft.Group.Assets).MinZ)
                    .ThenBy(candidate => candidate.Draft.GeneratorId, StringComparer.OrdinalIgnoreCase)
                    .First();
            }

            selected.Add(next);
            remaining.RemoveAll(candidate => ReferenceEquals(candidate.Draft, next.Draft));
        }

        return selected;
    }

    private static IEnumerable<ScoredDraft> OrderByRank(IReadOnlyList<ScoredDraft> ranked) =>
        ranked
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenBy(candidate => candidate.RawCost)
            .ThenBy(candidate => candidate.Draft.SourceAnchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => Footprint.From(candidate.Draft.Group.Assets).MinX)
            .ThenBy(candidate => Footprint.From(candidate.Draft.Group.Assets).MinZ)
            .ThenBy(candidate => candidate.Draft.GeneratorId, StringComparer.OrdinalIgnoreCase);

    private static ScoredDraft WithDiversity(
        ScoredDraft candidate,
        string reason,
        double normalizedBonus)
    {
        List<MetricValue> metrics = candidate.Metrics
            .Where(metric => metric.Id != DiversityMetricId)
            .ToList();
        metrics.Add(new MetricValue(
            Id: DiversityMetricId,
            RawValue: null,
            Unit: null,
            Normalized: normalizedBonus,
            Weight: ScoreWeights.Default.DiversityBonus,
            Contribution: normalizedBonus * ScoreWeights.Default.DiversityBonus,
            Better: "higher"));
        return candidate with
        {
            Metrics = metrics,
            DiversityReason = reason
        };
    }

    private static DiversityScore ScoreDiversity(
        ScoredDraft candidate,
        IReadOnlyList<ScoredDraft> selected)
    {
        if (selected.Any(chosen => !string.Equals(
                chosen.Draft.SourceAnchor.Anchor.RoomId,
                candidate.Draft.SourceAnchor.Anchor.RoomId,
                StringComparison.OrdinalIgnoreCase)))
        {
            return new DiversityScore("different_anchor", 1);
        }

        if (selected.Any(chosen => DoorRotation(chosen.Draft) != DoorRotation(candidate.Draft)))
            return new DiversityScore("different_orientation", 0.7);

        Footprint candidateFootprint = Footprint.From(candidate.Draft.Group.Assets);
        double farthestOriginDistance = selected
            .Select(chosen => candidateFootprint.OriginDistanceTo(Footprint.From(chosen.Draft.Group.Assets)))
            .DefaultIfEmpty(0)
            .Max();
        if (farthestOriginDistance >= 4)
            return new DiversityScore("different_origin", 0.45);

        return new DiversityScore("near_duplicate", 0);
    }

    private static int DoorRotation(PlacementDraft draft) =>
        draft.Group.Assets
            .Where(asset => asset.Role == "door")
            .Select(asset => asset.Rotation)
            .DefaultIfEmpty(0)
            .First();

    private sealed record DiversityScore(string Reason, double NormalizedBonus);
}
