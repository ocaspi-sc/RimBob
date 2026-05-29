using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Placement;
using RimBob.State;
using RimBob.State.Derivations.Common;

namespace RimBob.Ministers.Willie;

public sealed class PlacementSolver
{
    private const double PathCostWeight = 16;
    private readonly IPathCostProbe pathCostProbe;
    private readonly IPlacementValidator placementValidator;
    private readonly IReadOnlyList<IPlacementGenerator> generators;

    public PlacementSolver(
        IPathCostProbe pathCostProbe,
        IPlacementValidator placementValidator,
        IReadOnlyList<IPlacementGenerator>? generators = null)
    {
        this.pathCostProbe = pathCostProbe;
        this.placementValidator = placementValidator;
        this.generators = generators is { Count: > 0 }
            ? generators
            : [new TemplateAnchoredGenerator()];
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
            anchors);
        GenerationBudget budget = new(MaxDrafts: 1, MaxSearchRadius: 32);
        IReadOnlyList<PlacementDraft> drafts = generators
            .SelectMany(generator => generator.Generate(spec, evidence, budget))
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

        if (gatedDrafts.Count == 0)
        {
            return NoFit(
                NoFitReason.HardGateRejected,
                draftTraces,
                notes,
                "all drafts failed shared hard gates");
        }

        IReadOnlyList<ScoredDraft> scored = await ScoreByPathCostAsync(gatedDrafts, evidence.MapId, notes, ct);
        draftTraces.AddRange(scored.Select(score => TraceFor(score.Draft, "scored", null, score.Metrics)));
        if (scored.Count == 0)
        {
            return NoFit(
                NoFitReason.NoReachablePath,
                draftTraces,
                notes,
                "no reachable path from draft access cell to anchor target");
        }

        ScoredDraft best = scored
            .OrderBy(score => score.RawCost)
            .ThenBy(score => score.Draft.GeneratorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(score => score.Draft.Group.Assets.Min(asset => asset.Cell.X))
            .ThenBy(score => score.Draft.Group.Assets.Min(asset => asset.Cell.Z))
            .First();

        PlacementValidationResult validation = await placementValidator.ValidateAsync(best.Draft.Group, ct);
        if (!validation.CanPlaceAll || validation.OverlapConflicts.Count > 0)
        {
            draftTraces.Add(TraceFor(best.Draft, "validation_rejected", ValidationReason(validation), best.Metrics));
            return NoFit(
                NoFitReason.ValidationRejected,
                draftTraces,
                notes,
                ValidationReason(validation));
        }

        AdviceOption option = AssembleOption(spec, best, validation);
        PlacementReadiness materialsReady = MaterialReadiness(spec, validation.Cost);
        notes.Add("group_place_apply_out_of_scope");
        return new PlacementResult(
            Options: [option],
            Trace: new PlacementTrace("placement_solver", draftTraces, notes),
            NoFit: null,
            Draftable: PlacementReadiness.Ready,
            PlacementValid: PlacementReadiness.Ready,
            MaterialsReady: materialsReady,
            ApplyReady: PlacementReadiness.Blocked);
    }

    private async Task<IReadOnlyList<ScoredDraft>> ScoreByPathCostAsync(
        IReadOnlyList<PlacementDraft> drafts,
        int mapId,
        List<string> notes,
        CancellationToken ct)
    {
        IReadOnlyList<PathCostResult>? pathResults = await TryPathCostBatchAsync(drafts, mapId, notes, ct);
        List<ScoredDraft> scored = [];
        foreach (PlacementDraft draft in drafts)
        {
            if (pathResults is { Count: > 0 })
            {
                PathCostResult? reachable = pathResults
                    .Where(result =>
                        result.Reachable &&
                        draft.AccessCells.Contains(result.From) &&
                        result.To == draft.SourceAnchor.TargetCell.ToMapCell())
                    .OrderBy(result => result.Cost)
                    .FirstOrDefault();
                if (reachable is null) continue;

                scored.Add(ScoreDraft(draft, reachable.Cost, "path_tiles"));
                continue;
            }

            int fallbackDistance = draft.AccessCells
                .Select(cell => MapDistance.Manhattan(cell.ToMapPosition(), draft.SourceAnchor.TargetCell))
                .Min();
            scored.Add(ScoreDraft(draft, fallbackDistance, "tiles"));
        }

        return scored;
    }

    private async Task<IReadOnlyList<PathCostResult>?> TryPathCostBatchAsync(
        IReadOnlyList<PlacementDraft> drafts,
        int mapId,
        List<string> notes,
        CancellationToken ct)
    {
        IReadOnlyList<PathCostPair> pairs = drafts
            .SelectMany(draft => draft.AccessCells
                .Select(cell => new PathCostPair(cell, draft.SourceAnchor.TargetCell.ToMapCell())))
            .ToList();
        if (pairs.Count == 0) return null;

        try
        {
            return await pathCostProbe.GetPathCostsAsync(
                mapId,
                pairs,
                tier: "region",
                mode: "pass_doors",
                peMode: "on_cell",
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            notes.Add($"path_cost_fallback={ex.GetType().Name}");
            return null;
        }
    }

    private static ScoredDraft ScoreDraft(PlacementDraft draft, int rawCost, string unit)
    {
        double normalized = 1d / (1d + rawCost);
        MetricValue metric = new(
            Id: "freezer_to_kitchen_distance",
            RawValue: rawCost,
            Unit: unit,
            Normalized: normalized,
            Weight: PathCostWeight,
            Contribution: normalized * PathCostWeight,
            Better: "lower");
        return new ScoredDraft(draft, rawCost, [metric]);
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

        if (assetCells.Any(evidence.IsOccupied))
        {
            rejectionReason = "occupied_cell";
            return false;
        }

        if (assetCells.Distinct().Count() != assetCells.Count)
        {
            rejectionReason = "self_overlap";
            return false;
        }

        rejectionReason = null;
        return true;
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
        MetricValue metric = best.Metrics.Single(metric => metric.Id == "freezer_to_kitchen_distance");
        string target = best.Draft.SourceAnchor.Anchor.Class.ToString().ToLowerInvariant();
        return new AdviceOption(
            Id: $"placement_{spec.TargetClass.ToString().ToLowerInvariant()}_{firstCell.X}_{firstCell.Z}",
            Label: best.Draft.Group.Label,
            Summary: $"Validated {best.Draft.Group.Assets.Count}-asset freezer shell near {target}.",
            BlueprintGroup: best.Draft.Group,
            EstimatedMaterials: validation.Cost,
            TradeoffNote: $"Door-to-{target} score uses {metric.RawValue} {metric.Unit}.");
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
        IReadOnlyList<MetricValue> metrics) =>
        new(
            GeneratorId: draft.GeneratorId,
            AnchorRoomId: draft.SourceAnchor.Anchor.RoomId,
            Status: status,
            Reason: reason,
            Metrics: metrics);

    private sealed record ScoredDraft(
        PlacementDraft Draft,
        int RawCost,
        IReadOnlyList<MetricValue> Metrics);
}
