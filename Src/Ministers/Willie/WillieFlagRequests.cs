using RimBob.Core.Advice;

using RimBob.Core.Ministers;

namespace RimBob.Ministers.Willie;

public sealed record WillieFlagRequests(
    IReadOnlyList<BuildingRequest> BuildingRequests,
    IReadOnlyList<LaborRequest> LaborRequests,
    IReadOnlyList<ItemRequest> ItemRequests,
    IReadOnlyList<AttentionRequest> Attention)
{
    public static WillieFlagRequests Empty => new([], [], [], []);

    public int Count =>
        BuildingRequests.Count +
        LaborRequests.Count +
        ItemRequests.Count +
        Attention.Count;

    public IReadOnlyList<BuildingRequest>? BuildingRequestsOrNull => NullIfEmpty(BuildingRequests);
    public IReadOnlyList<LaborRequest>? LaborRequestsOrNull => NullIfEmpty(LaborRequests);
    public IReadOnlyList<ItemRequest>? ItemRequestsOrNull => NullIfEmpty(ItemRequests);
    public IReadOnlyList<AttentionRequest>? AttentionOrNull => NullIfEmpty(Attention);

    public static WillieFlagRequests Building(BuildingRequest request) =>
        Empty.Add(request);

    public static WillieFlagRequests Labor(LaborRequest request) =>
        Empty.Add(request);

    public static WillieFlagRequests Item(ItemRequest request) =>
        Empty.Add(request);

    public static WillieFlagRequests AttentionRequest(
        string request,
        string reason,
        Priority? priority,
        string? requestedFrom) =>
        Empty.Add(new AttentionRequest(
            Request: request,
            Reason: reason,
            Priority: priority,
            RequestedFrom: requestedFrom));

    public WillieFlagRequests Add(WillieFlagRequests other) =>
        new(
            BuildingRequests.Concat(other.BuildingRequests).ToArray(),
            LaborRequests.Concat(other.LaborRequests).ToArray(),
            ItemRequests.Concat(other.ItemRequests).ToArray(),
            Attention.Concat(other.Attention).ToArray());

    public WillieFlagRequests Add(BuildingRequest request) =>
        this with { BuildingRequests = BuildingRequests.Append(request).ToArray() };

    public WillieFlagRequests Add(LaborRequest request) =>
        this with { LaborRequests = LaborRequests.Append(request).ToArray() };

    public WillieFlagRequests Add(ItemRequest request) =>
        this with { ItemRequests = ItemRequests.Append(request).ToArray() };

    public WillieFlagRequests Add(AttentionRequest request) =>
        this with { Attention = Attention.Append(request).ToArray() };

    public IReadOnlyList<Decision> ToDecisions(RuleId rule, Priority fallbackPriority)
    {
        List<Decision> decisions = [];
        decisions.AddRange(BuildingRequests.Select(request => new RequestBuild(
            rule,
            request,
            request.RequestedFrom ?? "Willie",
            request.Priority ?? fallbackPriority)));
        decisions.AddRange(LaborRequests.Select(request => new RequestLabor(
            rule,
            request,
            request.RequestedFrom ?? "Labor",
            request.Priority ?? fallbackPriority)));
        decisions.AddRange(ItemRequests.Select(request => new RequestItem(
            rule,
            request,
            request.RequestedFrom ?? "Chief of Staff",
            request.Priority ?? fallbackPriority)));
        decisions.AddRange(Attention.Select(request => new RequestAttention(
            rule,
            request,
            request.RequestedFrom ?? "Chief of Staff",
            request.Priority ?? fallbackPriority)));
        return decisions;
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) =>
        values.Count == 0 ? null : values;
}
