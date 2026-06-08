using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Ministers.Willie;

public static class WillieAdviceComposer
{
    public static IReadOnlyList<AdviceItem> EnrichSolverAdvice(
        IReadOnlyList<AdviceItem> advice,
        PlacementSolveAttempt attempt,
        string drivingAdviceId,
        bool removeFallbackWhenApplyReady)
    {
        if (advice.Count == 0) return advice;

        return advice
            .Select(item => IsDrivingAdvice(item, drivingAdviceId)
                ? EnrichAdviceItem(item, attempt, removeFallbackWhenApplyReady)
                : item)
            .ToList();
    }

    public static IReadOnlyList<AdviceItem> MarkSolverPending(
        IReadOnlyList<AdviceItem> advice,
        string drivingAdviceId)
    {
        if (advice.Count == 0) return advice;

        return advice
            .Select(item => IsDrivingAdvice(item, drivingAdviceId)
                ? item with
                {
                    Body = AppendPlacementBodyNote(
                        item.Body,
                        "Layout options are computing in the background; this card will gain options automatically if the solve is still current."),
                    Rationale = AppendPlacementNote(
                        item.Rationale,
                        "Placement solve queued in the background.")
                }
                : item)
            .ToList();
    }

    public static AdviceItem EnrichAdviceItem(
        AdviceItem item,
        PlacementSolveAttempt attempt,
        bool removeFallbackWhenApplyReady)
    {
        if (attempt.Result is null)
            return item with
            {
                Body = AppendPlacementBodyNote(item.Body, attempt.AdviceBodyNote),
                Rationale = AppendPlacementNote(item.Rationale, attempt.Note)
            };

        PlacementResult result = attempt.Result;
        IReadOnlyList<AdviceOption>? options = result.Options.Count == 0
            ? item.Options
            : result.Options.Select(option =>
                AnnotateManualZonePlacement(option with
                {
                    Readiness = new AdviceOptionReadiness(
                        Draftable: WilliePlacementSolveText.ReadinessWire(result.Draftable),
                        PlacementValid: WilliePlacementSolveText.ReadinessWire(result.PlacementValid),
                        MaterialsReady: WilliePlacementSolveText.ReadinessWire(result.MaterialsReady),
                        ApplyReady: WilliePlacementSolveText.ReadinessWire(result.ApplyReady))
                }))
                .ToList();

        bool attachApplyActions = options is { Count: > 0 } &&
            result.ApplyReady != PlacementReadiness.Blocked;
        IReadOnlyList<AdviceAction> baseActions = removeFallbackWhenApplyReady && attachApplyActions
            ? item.Actions.Where(action => action.Kind != AdviceActionKind.PlaceBlueprint || action.Apply is not null).ToList()
            : item.Actions;
        IReadOnlyList<AdviceAction> actions = attachApplyActions
            ? baseActions.Concat(options!.Select(ApplyActionForOption).Where(action => action is not null).Cast<AdviceAction>()).ToList()
            : baseActions;

        return item with
        {
            Body = AppendPlacementBodyNote(item.Body, attempt.AdviceBodyNote),
            Options = options,
            Actions = actions,
            Rationale = AppendPlacementNote(item.Rationale, attempt.Note)
        };
    }

    public static bool HasApplyResult(AdviceItem item) =>
        item.Actions.Any(action => action.ApplyResult is not null);

    private static bool IsDrivingAdvice(AdviceItem item, string drivingAdviceId) =>
        string.Equals(item.Id, drivingAdviceId, StringComparison.OrdinalIgnoreCase);

    private static AdviceAction? ApplyActionForOption(AdviceOption option)
    {
        if (IsZoneCellOption(option))
            return GrowingZoneApplyActionForOption(option);

        return new AdviceAction(
            AdviceActionKind.PlaceBlueprint,
            $"Place the {option.Label} blueprint group.",
            Owner: "Willie",
            Apply: new PlaceBlueprintGroupApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                BlueprintGroup: option.BlueprintGroup,
                AssetCount: option.BlueprintGroup.Assets.Count));
    }

    private static AdviceAction? GrowingZoneApplyActionForOption(AdviceOption option)
    {
        ZoneOptionShape? shape = ZoneOptionShapeFor(option);
        if (shape is null)
            return null;

        if (shape.Rect.Area != shape.Cells.Count)
            return null;

        return new AdviceAction(
            AdviceActionKind.DesignateZoneReq,
            $"Create the {option.Label} growing zone.",
            Quantity: shape.Cells.Count,
            Owner: "Willie",
            WorkType: WorkType.Grow,
            Apply: new CreateGrowingZoneApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                PlantDef: shape.PlantDef,
                Rect: shape.Rect,
                TargetCount: shape.Cells.Count));
    }

    private static AdviceOption AnnotateManualZonePlacement(AdviceOption option)
    {
        ZoneOptionShape? shape = ZoneOptionShapeFor(option);
        if (shape is null || shape.Rect.Area == shape.Cells.Count)
            return option;

        return option with
        {
            TradeoffNote = AppendPlacementNote(
                option.TradeoffNote ?? string.Empty,
                "Non-rectangular growing area - place it manually in-game.")
        };
    }

    private static ZoneOptionShape? ZoneOptionShapeFor(AdviceOption option)
    {
        IReadOnlyList<BlueprintAsset> zoneAssets = option.BlueprintGroup.Assets
            .Where(asset => string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (zoneAssets.Count == 0 || zoneAssets.Count != option.BlueprintGroup.Assets.Count)
            return null;

        IReadOnlyList<string> plantDefs = zoneAssets
            .Select(asset => asset.DefName)
            .Where(def => !string.IsNullOrWhiteSpace(def))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (plantDefs.Count != 1)
            return null;

        IReadOnlyList<MapCell> cells = zoneAssets
            .Select(asset => asset.Cell)
            .Distinct()
            .ToList();
        if (cells.Count != zoneAssets.Count)
            return null;

        MapRect rect = new(
            X1: cells.Min(cell => cell.X),
            Z1: cells.Min(cell => cell.Z),
            X2: cells.Max(cell => cell.X),
            Z2: cells.Max(cell => cell.Z));

        return new ZoneOptionShape(plantDefs[0], cells, rect);
    }

    private static bool IsZoneCellOption(AdviceOption option) =>
        option.BlueprintGroup.Assets.Any(asset =>
            string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase));

    private static string AppendPlacementNote(string text, string note) =>
        string.IsNullOrWhiteSpace(text) ? note : $"{text} {note}";

    private static string AppendPlacementBodyNote(string body, string? note) =>
        string.IsNullOrWhiteSpace(note) ? body : AppendPlacementNote(body, note);

    private sealed record ZoneOptionShape(
        string PlantDef,
        IReadOnlyList<MapCell> Cells,
        MapRect Rect);
}
