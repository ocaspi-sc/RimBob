using System.Globalization;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Placement;
using RimBob.State;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public interface IPlacementSolver
{
    Task<PlacementResult> SolveAsync(
        PlacementSpec spec,
        WillieBriefing briefing,
        ColonyState colonyState,
        CancellationToken ct = default);
}

public sealed class PlacementSolver : IPlacementSolver
{
    private const int MaxValidateCount = 3;
    private const int MaxOptionCount = 3;
    private readonly IPathCostProbe pathCostProbe;
    private readonly IPlacementValidator placementValidator;
    private readonly IReadOnlyList<IPlacementGenerator> generators;
    private readonly IPlacementScorer scorer;

    public PlacementSolver(
        IPathCostProbe pathCostProbe,
        IPlacementValidator placementValidator,
        IReadOnlyList<IPlacementGenerator>? generators = null,
        IPlacementScorer? scorer = null)
    {
        this.pathCostProbe = pathCostProbe;
        this.placementValidator = placementValidator;
        this.generators = generators is { Count: > 0 }
            ? generators
            : [new ReuseExistingFootprintGenerator(), new TemplateAnchoredGenerator(), new LargestEmptyRectangleGenerator()];
        this.scorer = scorer ?? new WalkablePathCostScorer();
    }

    public async Task<PlacementResult> SolveAsync(
        PlacementSpec spec,
        WillieBriefing briefing,
        ColonyState colonyState,
        CancellationToken ct = default)
    {
        List<string> notes = [];
        List<PlacementDraftTrace> draftTraces = [];
        IReadOnlyList<ResolvedAnchor> anchors = AnchorResolver.ResolveNear(spec, briefing);
        if (anchors.Count == 0)
        {
            return NoFit(
                NoFitReason.NoAnchors,
                draftTraces,
                notes,
                "no resolved near-anchor with a target cell");
        }

        PlacementEvidence evidence = PlacementEvidence.Build(
            colonyState.Map.Value,
            colonyState.Buildings.Value,
            anchors,
            briefing.AnchorInventory.Anchors);
        IReadOnlyList<PlacementDraft> drafts = generators
            .SelectMany(generator => generator.Generate(spec, evidence, BudgetFor(generator)))
            .ToList();
        if (drafts.Count == 0)
        {
            return NoFit(
                NoFitReason.NoDrafts,
                draftTraces,
                notes,
                "generators emitted no drafts");
        }

        List<PlacementDraft> gatedDrafts = [];
        foreach (PlacementDraft draft in drafts)
        {
            if (PassesHardGate(draft, evidence, out string? rejectionReason))
            {
                gatedDrafts.Add(draft);
                continue;
            }

            draftTraces.Add(TraceFor(draft, "rejected", rejectionReason, []));
        }

        IReadOnlyList<PlacementDraft> uniqueDrafts = DraftDedupe.ByCellsShapeAnchor(gatedDrafts);
        if (gatedDrafts.Count == 0)
        {
            return NoFit(
                NoFitReason.HardGateRejected,
                draftTraces,
                notes,
                "all drafts failed shared hard gates");
        }

        PathCostLookup pathCosts = await BuildPathCostLookupAsync(uniqueDrafts, evidence.MapId, notes, ct);
        IReadOnlyList<ScoredDraft> scored = RankScored(scorer.Score(uniqueDrafts, pathCosts, evidence)).ToList();
        draftTraces.AddRange(scored.Select(score => TraceFor(score.Draft, "scored", null, score.Metrics)));
        if (scored.Count == 0)
        {
            return NoFit(
                NoFitReason.NoReachablePath,
                draftTraces,
                notes,
                "no reachable path from draft access cell to anchor target");
        }

        IReadOnlyList<ScoredDraft> selected = DiverseSelector.SelectTopK(scored, MaxValidateCount);
        draftTraces.AddRange(selected.Select(score =>
            TraceFor(score.Draft, "selected", null, score.Metrics, score.DiversityReason)));
        List<ValidatedScoredDraft> validated = [];
        foreach (ScoredDraft selectedDraft in selected)
        {
            PlacementValidationResult validation = await placementValidator.ValidateAsync(selectedDraft.Draft.Group, ct);
            if (!validation.CanPlaceAll || validation.OverlapConflicts.Count > 0)
            {
                draftTraces.Add(TraceFor(
                    selectedDraft.Draft,
                    "validation_rejected",
                    ValidationReason(validation),
                    selectedDraft.Metrics,
                    selectedDraft.DiversityReason));
                continue;
            }

            ScoredDraft withMaterialCost = AddMaterialCostMetric(selectedDraft, validation.Cost);
            validated.Add(new ValidatedScoredDraft(withMaterialCost, validation));
            draftTraces.Add(TraceFor(
                withMaterialCost.Draft,
                "validated",
                null,
                withMaterialCost.Metrics,
                withMaterialCost.DiversityReason));
        }

        if (validated.Count == 0)
        {
            return NoFit(
                NoFitReason.ValidationRejected,
                draftTraces,
                notes,
                "all selected drafts failed fork validation");
        }

        IReadOnlyList<ValidatedScoredDraft> finalCandidates = validated
            .OrderByDescending(candidate => candidate.Score.TotalScore)
            .ThenBy(candidate => candidate.Score.RawCost)
            .ThenBy(candidate => candidate.Score.Draft.SourceAnchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => Footprint.From(candidate.Score.Draft.Group.Assets).MinX)
            .ThenBy(candidate => Footprint.From(candidate.Score.Draft.Group.Assets).MinZ)
            .ThenBy(candidate => candidate.Score.Draft.GeneratorId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxOptionCount)
            .ToList();
        IReadOnlyList<AdviceOption> options = finalCandidates
            .Select(candidate => AssembleOption(spec, candidate.Score, candidate.Validation))
            .ToList();
        PlacementReadiness materialsReady = MaterialReadiness(spec, finalCandidates.Select(candidate => candidate.Validation.Cost));
        notes.Add("group_place_apply_out_of_scope");
        return new PlacementResult(
            Options: options,
            Trace: new PlacementTrace("placement_solver", draftTraces, notes),
            NoFit: null,
            Draftable: PlacementReadiness.Ready,
            PlacementValid: PlacementReadiness.Ready,
            MaterialsReady: materialsReady,
            ApplyReady: PlacementReadiness.Blocked);
    }

    private static GenerationBudget BudgetFor(IPlacementGenerator generator) =>
        string.Equals(generator.Id, "template_anchored", StringComparison.OrdinalIgnoreCase)
            ? new GenerationBudget(MaxDrafts: 4, MaxSearchRadius: 32)
            : new GenerationBudget(MaxDrafts: 4, MaxSearchRadius: 32);

    private async Task<PathCostLookup> BuildPathCostLookupAsync(
        IReadOnlyList<PlacementDraft> drafts,
        int mapId,
        List<string> notes,
        CancellationToken ct)
    {
        IReadOnlyList<PathCostPair> pairs = PathCostLookup.RequestPairsFor(drafts);
        if (pairs.Count == 0) return PathCostLookup.ProbeUnavailable(drafts);

        try
        {
            IReadOnlyList<PathCostResult> results = await pathCostProbe.GetPathCostsAsync(
                mapId,
                pairs,
                tier: "region",
                mode: "pass_doors",
                peMode: "on_cell",
                ct: ct);
            if (results.Count == 0) return PathCostLookup.ProbeUnavailable(drafts);

            return PathCostLookup.FromProbeResults(drafts, results);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"path_cost_fallback={ex.GetType().Name}");
            return PathCostLookup.ProbeUnavailable(drafts);
        }
    }

    private static bool PassesHardGate(
        PlacementDraft draft,
        PlacementEvidence evidence,
        out string? rejectionReason)
    {
        IReadOnlyList<MapCell> assetCells = draft.Group.Assets.Select(asset => asset.Cell).ToList();
        if (assetCells.Any(cell => !evidence.InBounds(cell)))
        {
            rejectionReason = "out_of_bounds";
            return false;
        }

        if (draft.AccessCells.Any(cell => !evidence.InBounds(cell)))
        {
            rejectionReason = "access_out_of_bounds";
            return false;
        }

        if (assetCells.Any(evidence.IsOccupied))
        {
            rejectionReason = "occupied_cell";
            return false;
        }

        if (draft.AccessCells.Any(evidence.IsOccupied))
        {
            rejectionReason = "access_occupied";
            return false;
        }

        IReadOnlyList<MapCell> nonFloorCells = draft.Group.Assets
            .Where(asset => !string.Equals(asset.Role, "floor", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.Cell)
            .ToList();
        if (nonFloorCells.Distinct().Count() != nonFloorCells.Count)
        {
            rejectionReason = "self_overlap";
            return false;
        }

        rejectionReason = null;
        return true;
    }

    private static IReadOnlyList<ScoredDraft> RankScored(IReadOnlyList<ScoredDraft> scored) =>
        scored
            .OrderByDescending(score => score.TotalScore)
            .ThenBy(score => score.RawCost)
            .ThenBy(score => score.Draft.SourceAnchor.Anchor.RoomId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(score => Footprint.From(score.Draft.Group.Assets).MinX)
            .ThenBy(score => Footprint.From(score.Draft.Group.Assets).MinZ)
            .ThenBy(score => score.Draft.GeneratorId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ScoredDraft AddMaterialCostMetric(
        ScoredDraft score,
        IReadOnlyList<MaterialEstimate> cost)
    {
        int rawCost = cost.Sum(material => material.Count);
        double normalized = 1d / (1d + rawCost);
        List<MetricValue> metrics = [.. score.Metrics];
        metrics.Add(new MetricValue(
            Id: "material_cost",
            RawValue: rawCost,
            Unit: "items",
            Normalized: normalized,
            Weight: ScoreWeights.Default.MaterialCost,
            Contribution: normalized * ScoreWeights.Default.MaterialCost,
            Better: "lower"));
        return score with { Metrics = metrics };
    }

    private static AdviceOption AssembleOption(
        PlacementSpec spec,
        ScoredDraft best,
        PlacementValidationResult validation)
    {
        MapCell firstCell = best.Draft.Group.Assets
            .OrderBy(asset => asset.Cell.X)
            .ThenBy(asset => asset.Cell.Z)
            .First()
            .Cell;
        string target = best.Draft.SourceAnchor.Anchor.Class.ToString().ToLowerInvariant();
        return new AdviceOption(
            Id: $"placement_{spec.TargetClass.ToString().ToLowerInvariant()}_{firstCell.X}_{firstCell.Z}",
            Label: best.Draft.Group.Label,
            Summary: $"Validated {best.Draft.Group.Assets.Count}-asset {best.Draft.Group.Label.ToLowerInvariant()} near {target}.",
            BlueprintGroup: best.Draft.Group,
            EstimatedMaterials: validation.Cost,
            TradeoffNote: TradeoffNoteFor(best, target));
    }

    private static string TradeoffNoteFor(ScoredDraft score, string target)
    {
        MetricValue metric = WinningTradeoffMetric(score.Metrics);
        Footprint footprint = Footprint.From(score.Draft.Group.Assets);
        string footprintText = $"footprint {footprint.MinX},{footprint.MinZ}";
        string rawValue = FormatRawValue(metric.RawValue);
        string unit = metric.Unit is null ? string.Empty : $" {metric.Unit}";
        return metric.Id switch
        {
            "freezer_to_kitchen_distance" => $"Closest to {target}: {rawValue}{unit} from door ({footprintText}).",
            "expansion_room" => $"Most room to expand: {rawValue}{unit} around {footprintText}.",
            "build_order_safety" => $"Safest build order: {rawValue}{unit} safety around {footprintText}.",
            "material_cost" => $"Cheapest materials: {rawValue}{unit} estimated for {footprintText}.",
            _ => $"Best weighted score: {metric.Id} {rawValue}{unit} at {footprintText}."
        };
    }

    private static MetricValue WinningTradeoffMetric(IReadOnlyList<MetricValue> metrics) =>
        metrics
            .Where(metric => TradeoffMetricPriority(metric.Id) < int.MaxValue)
            .OrderByDescending(metric => metric.Contribution)
            .ThenBy(metric => TradeoffMetricPriority(metric.Id))
            .FirstOrDefault()
        ?? metrics
            .OrderByDescending(metric => metric.Contribution)
            .ThenBy(metric => metric.Id, StringComparer.Ordinal)
            .First();

    private static int TradeoffMetricPriority(string metricId) =>
        metricId switch
        {
            "freezer_to_kitchen_distance" => 0,
            "expansion_room" => 1,
            "build_order_safety" => 2,
            "material_cost" => 3,
            _ => int.MaxValue
        };

    private static string FormatRawValue(double? rawValue)
    {
        if (rawValue is null) return "unknown";

        double value = rawValue.Value;
        return Math.Abs(value - Math.Round(value)) < 0.0001
            ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static PlacementReadiness MaterialReadiness(
        PlacementSpec spec,
        IReadOnlyList<MaterialEstimate> cost)
    {
        if (cost.Count == 0) return PlacementReadiness.Ready;
        if (spec.MaterialsOnHand.Count == 0) return PlacementReadiness.Unknown;

        foreach (MaterialEstimate required in cost)
        {
            int available = spec.MaterialsOnHand
                .Where(material => string.Equals(material.Material, required.DefName, StringComparison.OrdinalIgnoreCase))
                .Sum(material => material.ApproxQty ?? 0);
            if (available < required.Count)
                return PlacementReadiness.Blocked;
        }

        return PlacementReadiness.Ready;
    }

    private static PlacementReadiness MaterialReadiness(
        PlacementSpec spec,
        IEnumerable<IReadOnlyList<MaterialEstimate>> costs)
    {
        List<PlacementReadiness> readiness = costs
            .Select(cost => MaterialReadiness(spec, cost))
            .ToList();
        if (readiness.Any(state => state == PlacementReadiness.Ready))
            return PlacementReadiness.Ready;
        if (readiness.Any(state => state == PlacementReadiness.Unknown))
            return PlacementReadiness.Unknown;
        return PlacementReadiness.Blocked;
    }

    private static PlacementResult NoFit(
        NoFitReason reason,
        IReadOnlyList<PlacementDraftTrace> draftTraces,
        IReadOnlyList<string> notes,
        string note)
    {
        List<string> mergedNotes = [.. notes, note];
        return new PlacementResult(
            Options: [],
            Trace: new PlacementTrace("placement_solver", draftTraces, mergedNotes),
            NoFit: reason,
            Draftable: reason is NoFitReason.NoAnchors or NoFitReason.NoDrafts or NoFitReason.HardGateRejected
                ? PlacementReadiness.Blocked
                : PlacementReadiness.Ready,
            PlacementValid: PlacementReadiness.Blocked,
            MaterialsReady: PlacementReadiness.Unknown,
            ApplyReady: PlacementReadiness.Blocked);
    }

    private static string ValidationReason(PlacementValidationResult validation) =>
        validation.OverlapConflicts.Count > 0
            ? "overlap_conflicts"
            : "can_place_all_false";

    private static PlacementDraftTrace TraceFor(
        PlacementDraft draft,
        string status,
        string? reason,
        IReadOnlyList<MetricValue> metrics,
        string? diversityReason = null) =>
        new(
            GeneratorId: draft.GeneratorId,
            AnchorRoomId: draft.SourceAnchor.Anchor.RoomId,
            Status: status,
            Reason: reason,
            Metrics: metrics,
            DiversityReason: diversityReason);

    private sealed record ValidatedScoredDraft(
        ScoredDraft Score,
        PlacementValidationResult Validation);
}
