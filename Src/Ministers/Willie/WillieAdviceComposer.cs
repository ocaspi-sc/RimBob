using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Ministers.Willie;

public static class WillieAdviceComposer
{
    private const string StockpileZoneOptionPrefix = "zone_stockpile_";
    private static readonly IReadOnlyList<string> FoodStockpileCategories = ["Foods"];

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
        ZoneOptionShape? zoneShape = ZoneOptionShapeFor(option);
        if (zoneShape is not null)
            return zoneShape.ZoneClass == ZoneClass.Stockpile
                ? StockpileZoneApplyActionForOption(option, zoneShape)
                : GrowingZoneApplyActionForOption(option, zoneShape);
        if (HasZoneCellAsset(option))
            return null;

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

    private static AdviceAction? GrowingZoneApplyActionForOption(AdviceOption option, ZoneOptionShape shape)
    {
        if (shape.Rect.Area != shape.Cells.Count)
            return null;

        if (string.IsNullOrWhiteSpace(shape.ZoneDef))
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
                PlantDef: shape.ZoneDef,
                Rect: shape.Rect,
                TargetCount: shape.Cells.Count));
    }

    private static AdviceAction? StockpileZoneApplyActionForOption(AdviceOption option, ZoneOptionShape shape)
    {
        if (shape.Rect.Area != shape.Cells.Count)
            return null;

        return new AdviceAction(
            AdviceActionKind.SetStockpileZone,
            $"Create the {option.Label} food stockpile zone.",
            Quantity: shape.Cells.Count,
            Owner: "Willie",
            WorkType: WorkType.Haul,
            Apply: new CreateStockpileZoneApply(
                Label: option.Label,
                TargetSummary: option.Summary,
                MapId: option.BlueprintGroup.MapId,
                Rect: shape.Rect,
                TargetCount: shape.Cells.Count,
                Name: option.Label,
                Priority: 0,
                AllowedItemCategories: FoodStockpileCategories));
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
                $"Non-rectangular {ZoneClassLabel(shape.ZoneClass)} area - place it manually in-game.")
        };
    }

    private static ZoneOptionShape? ZoneOptionShapeFor(AdviceOption option)
    {
        IReadOnlyList<BlueprintAsset> zoneAssets = option.BlueprintGroup.Assets
            .Where(asset => string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (zoneAssets.Count == 0 || zoneAssets.Count != option.BlueprintGroup.Assets.Count)
            return null;

        IReadOnlyList<string> zoneDefs = zoneAssets
            .Select(asset => asset.DefName)
            .Where(def => !string.IsNullOrWhiteSpace(def))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (zoneDefs.Count != 1)
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

        return new ZoneOptionShape(
            OptionZoneClass(option),
            zoneDefs[0],
            cells,
            rect);
    }

    private static ZoneClass OptionZoneClass(AdviceOption option) =>
        option.Id.StartsWith(StockpileZoneOptionPrefix, StringComparison.OrdinalIgnoreCase)
            ? ZoneClass.Stockpile
            : ZoneClass.Growing;

    private static bool HasZoneCellAsset(AdviceOption option) =>
        option.BlueprintGroup.Assets.Any(asset =>
            string.Equals(asset.Role, "zone_cell", StringComparison.OrdinalIgnoreCase));

    private static string ZoneClassLabel(ZoneClass zoneClass) =>
        zoneClass == ZoneClass.Stockpile ? "stockpile" : "growing";

    private static string AppendPlacementNote(string text, string note) =>
        string.IsNullOrWhiteSpace(text) ? note : $"{text} {note}";

    private static string AppendPlacementBodyNote(string body, string? note) =>
        string.IsNullOrWhiteSpace(note) ? body : AppendPlacementNote(body, note);

    private sealed record ZoneOptionShape(
        ZoneClass ZoneClass,
        string ZoneDef,
        IReadOnlyList<MapCell> Cells,
        MapRect Rect);
}
