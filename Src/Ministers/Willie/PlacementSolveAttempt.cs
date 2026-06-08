using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Ministers.Willie;

public sealed record PlacementSolveAttempt(
    PlacementResult? Result,
    string Note,
    string? AdviceBodyNote,
    PlacementSolverReplayOutput ReplayOutput,
    bool SolverOffline)
{
    public bool IsTerminal =>
        ReplayOutput.Status is "options" or "no_fit" or "error";

    public static PlacementSolveAttempt FromResult(PlacementResult result, BuildingRequest request) =>
        new(
            result,
            WilliePlacementSolveText.NoteFor(result, request),
            WilliePlacementSolveText.AdviceBodyNoteFor(result, request),
            PlacementSolverReplayOutput.FromResult(result),
            SolverOffline: false);

    public static PlacementSolveAttempt FromCached(CachedSolveOutcome outcome, BuildingRequest request) =>
        new(
            outcome.Result,
            WilliePlacementSolveText.CachedNoteFor(outcome.Result, request),
            WilliePlacementSolveText.AdviceBodyNoteFor(outcome.Result, request),
            outcome.ReplayOutput,
            SolverOffline: false);

    public static PlacementSolveAttempt FromZoneResult(PlacementResult result, ZoneRequest request) =>
        new(
            result,
            WilliePlacementSolveText.NoteFor(result, request),
            WilliePlacementSolveText.AdviceBodyNoteFor(result, request),
            PlacementSolverReplayOutput.FromResult(result),
            SolverOffline: false);

    public static PlacementSolveAttempt FromCached(CachedSolveOutcome outcome, ZoneRequest request) =>
        new(
            outcome.Result,
            WilliePlacementSolveText.CachedNoteFor(outcome.Result, request),
            WilliePlacementSolveText.AdviceBodyNoteFor(outcome.Result, request),
            outcome.ReplayOutput,
            SolverOffline: false);

    public static PlacementSolveAttempt FromFailure(Exception ex, string? prefix = null)
    {
        string errorType = ex.GetType().Name;
        string notePrefix = string.IsNullOrWhiteSpace(prefix) ? "Placement solver" : prefix;
        string bodyPrefix = string.IsNullOrWhiteSpace(prefix) ? "Placement solver" : prefix;
        return new PlacementSolveAttempt(
            null,
            $"{notePrefix} unavailable: {errorType}. Keeping prose advice.",
            $"{bodyPrefix} could not suggest layout options because it hit {errorType} before validation completed.",
            PlacementSolverReplayOutput.FromFailure(errorType, ex.Message),
            SolverOffline: false);
    }

    public static PlacementSolveAttempt FromSolverOffline(Exception ex)
    {
        string errorType = ex.GetType().Name;
        return new PlacementSolveAttempt(
            null,
            $"Placement solver offline: {errorType}. Keeping prose advice while background solve records the failure.",
            $"Placement solver could not refresh layout options because live map validation was unavailable ({errorType}).",
            PlacementSolverReplayOutput.FromFailure(errorType, ex.Message),
            SolverOffline: true);
    }

    public static PlacementSolveAttempt FromZoneFailure(Exception ex)
    {
        string errorType = ex.GetType().Name;
        return new PlacementSolveAttempt(
            null,
            $"Grow-zone solver unavailable: {errorType}. Keeping prose advice.",
            $"Grow-zone solver could not suggest zone options because it hit {errorType} before scoring completed.",
            PlacementSolverReplayOutput.FromFailure(errorType, ex.Message),
            SolverOffline: false);
    }
}

public sealed record PlacementSolverReplayOutput(
    string Status,
    string? NoFit,
    string? Draftable,
    string? PlacementValid,
    string? MaterialsReady,
    string? ApplyReady,
    PlacementTrace? Trace,
    string? ErrorType,
    string? ErrorMessage)
{
    public static PlacementSolverReplayOutput FromResult(PlacementResult result) =>
        new(
            Status: result.Options.Count > 0 ? "options" : "no_fit",
            NoFit: result.NoFit?.ToString(),
            Draftable: result.Draftable.ToString(),
            PlacementValid: result.PlacementValid.ToString(),
            MaterialsReady: result.MaterialsReady.ToString(),
            ApplyReady: result.ApplyReady.ToString(),
            Trace: result.Trace,
            ErrorType: null,
            ErrorMessage: null);

    public static PlacementSolverReplayOutput FromFailure(string errorType, string errorMessage) =>
        new(
            Status: "error",
            NoFit: null,
            Draftable: null,
            PlacementValid: null,
            MaterialsReady: null,
            ApplyReady: null,
            Trace: null,
            ErrorType: errorType,
            ErrorMessage: errorMessage);

    public static PlacementSolverReplayOutput FromLifecycle(string status, string? message = null) =>
        new(
            Status: status,
            NoFit: null,
            Draftable: null,
            PlacementValid: null,
            MaterialsReady: null,
            ApplyReady: null,
            Trace: null,
            ErrorType: null,
            ErrorMessage: message);
}
