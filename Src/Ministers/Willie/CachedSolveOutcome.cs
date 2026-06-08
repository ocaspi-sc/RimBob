using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed record CachedSolveOutcome(
    PlacementResult Result,
    PlacementSolverReplayOutput ReplayOutput)
{
    public static CachedSolveOutcome? From(WillieSolverSnapshot snapshot) =>
        IsReusable(snapshot.Output)
            ? new CachedSolveOutcome(ResultFromReplay(snapshot.Output, snapshot.Options), snapshot.Output)
            : null;

    public static CachedSolveOutcome? From(WillieZoneSolverSnapshot snapshot) =>
        IsReusable(snapshot.Output)
            ? new CachedSolveOutcome(ResultFromReplay(snapshot.Output, snapshot.Options), snapshot.Output)
            : null;

    private static bool IsReusable(PlacementSolverReplayOutput output) =>
        string.Equals(output.Status, "options", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(output.Status, "no_fit", StringComparison.OrdinalIgnoreCase);

    private static PlacementResult ResultFromReplay(
        PlacementSolverReplayOutput output,
        IReadOnlyList<AdviceOption> options) =>
        new(
            Options: options,
            Trace: output.Trace ?? new PlacementTrace("cached_placement_solver", [], ["reused cached solver outcome"]),
            NoFit: ParseNoFit(output.NoFit),
            Draftable: ParseReadiness(output.Draftable),
            PlacementValid: ParseReadiness(output.PlacementValid),
            MaterialsReady: ParseReadiness(output.MaterialsReady),
            ApplyReady: ParseReadiness(output.ApplyReady));

    private static PlacementReadiness ParseReadiness(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out PlacementReadiness readiness)
            ? readiness
            : PlacementReadiness.Unknown;

    private static NoFitReason? ParseNoFit(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out NoFitReason reason)
            ? reason
            : null;
}
