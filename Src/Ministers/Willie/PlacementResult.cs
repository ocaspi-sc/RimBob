using RimBob.Core.Advice;

namespace RimBob.Ministers.Willie;

public sealed record PlacementResult(
    IReadOnlyList<AdviceOption> Options,
    PlacementTrace Trace,
    NoFitReason? NoFit,
    PlacementReadiness Draftable,
    PlacementReadiness PlacementValid,
    PlacementReadiness MaterialsReady,
    PlacementReadiness ApplyReady);

public sealed record PlacementTrace(
    string SelectedRule,
    IReadOnlyList<PlacementDraftTrace> Drafts,
    IReadOnlyList<string> Notes);

public sealed record PlacementDraftTrace(
    string GeneratorId,
    string AnchorRoomId,
    string Status,
    string? Reason,
    IReadOnlyList<MetricValue> Metrics);

public sealed record MetricValue(
    string Id,
    double? RawValue,
    string? Unit,
    double Normalized,
    double Weight,
    double Contribution,
    string Better);

public enum PlacementReadiness
{
    Unknown,
    Ready,
    Blocked
}

public enum NoFitReason
{
    NoAnchors,
    NoDrafts,
    HardGateRejected,
    NoReachablePath,
    ValidationRejected
}
