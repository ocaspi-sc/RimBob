using RimBob.Core.Advice;
using RimBob.Core.Briefings;

namespace RimBob.Core.Ministers;

public sealed record DecisionProjectionContext(
    string Minister,
    string Domain,
    long BriefingVersion,
    GameDate? GameDate,
    long? GameTick,
    DateTimeOffset Now);

public static class DecisionProjection
{
    public static IReadOnlyList<AdviceItem> ProjectAdvice(
        IReadOnlyList<Decision> decisions,
        DecisionProjectionContext context) =>
        decisions
            .OfType<Advise>()
            .Select(advice => ProjectAdvice(advice, context))
            .ToList();

    public static AdviceItem ProjectAdvice(Advise advice, DecisionProjectionContext context)
    {
        DateTimeOffset expiresAt = context.Now.AddHours(advice.Priority >= Priority.High ? 4 : 24);
        long? expiresGameTick = context.GameTick is { } gameTick
            ? AdviceFreshness.ExpiresGameTick(gameTick, advice.Priority)
            : null;

        return new AdviceItem(
            Id: $"{AdviceIdPrefix(context.Minister)}_{advice.Rule.Value}",
            Minister: context.Minister,
            Priority: advice.Priority,
            Title: advice.Title,
            Body: advice.Body,
            Rationale: advice.Rationale,
            Actions: advice.Actions,
            GuideCitationIds: advice.GuideCitationIds ?? [],
            Stamp: new AdviceStamp(
                context.Now,
                expiresAt,
                context.GameDate,
                context.GameTick,
                expiresGameTick),
            BriefingRef: new BriefingRef(context.Minister, context.BriefingVersion, $"{context.Domain}:{context.BriefingVersion}"),
            Options: advice.Options);
    }

    public static IReadOnlyList<AgentFlag> ProjectFlags(
        IReadOnlyList<Decision> decisions,
        DecisionProjectionContext context)
    {
        Dictionary<RuleId, Advise> adviceByRule = decisions
            .OfType<Advise>()
            .GroupBy(advice => advice.Rule)
            .ToDictionary(group => group.Key, group => group.First());
        List<AgentFlag> flags = [];

        foreach (IGrouping<RuleId, Decision> group in decisions
                     .Where(IsRequest)
                     .GroupBy(decision => decision.Rule))
        {
            List<BuildingRequest> buildingRequests = [];
            List<LaborRequest> laborRequests = [];
            List<ItemRequest> itemRequests = [];
            List<AttentionRequest> attentionRequests = [];
            Priority priority = group.Select(RequestPriority).DefaultIfEmpty(Priority.Low).Max();

            foreach (Decision decision in group)
            {
                switch (decision)
                {
                    case RequestBuild request:
                        buildingRequests.Add(request.Request with
                        {
                            Priority = request.Request.Priority ?? request.Priority,
                            RequestedFrom = CleanTo(request.To)
                        });
                        break;
                    case RequestLabor request:
                        laborRequests.Add(request.Request with
                        {
                            Priority = request.Request.Priority ?? request.Priority,
                            RequestedFrom = CleanTo(request.To)
                        });
                        break;
                    case RequestItem request:
                        itemRequests.Add(request.Request with
                        {
                            Priority = request.Request.Priority ?? request.Priority,
                            RequestedFrom = CleanTo(request.To)
                        });
                        break;
                    case RequestAttention request:
                        attentionRequests.Add(request.Request with
                        {
                            Priority = request.Request.Priority ?? request.Priority,
                            RequestedFrom = CleanTo(request.To)
                        });
                        break;
                }
            }

            string summary = adviceByRule.TryGetValue(group.Key, out Advise? advice)
                ? advice.Title
                : HumanizeRule(group.Key.Value);

            flags.Add(new AgentFlag(
                Id: $"{context.Domain}:{group.Key.Value}",
                SourceMinister: context.Minister,
                Priority: priority,
                Domain: context.Domain,
                Summary: summary,
                BuildingRequests: NullIfEmpty(buildingRequests),
                LaborRequests: NullIfEmpty(laborRequests),
                ItemRequests: NullIfEmpty(itemRequests),
                Attention: NullIfEmpty(attentionRequests),
                Detail: group.Key.Value,
                ExpiresAt: context.Now.AddHours(24)));
        }

        return flags;
    }

    private static bool IsRequest(Decision decision) =>
        decision is RequestBuild or RequestLabor or RequestItem or RequestAttention;

    private static Priority RequestPriority(Decision decision) =>
        decision switch
        {
            RequestBuild request => request.Priority,
            RequestLabor request => request.Priority,
            RequestItem request => request.Priority,
            RequestAttention request => request.Priority,
            _ => Priority.Low
        };

    private static string CleanTo(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Chief of Staff" : value.Trim();

    private static string AdviceIdPrefix(string minister) =>
        minister.Trim().ToLowerInvariant().Replace(" ", "_", StringComparison.Ordinal);

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) =>
        values.Count == 0 ? null : values;

    private static string HumanizeRule(string value) =>
        string.Join(
            " ",
            value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
