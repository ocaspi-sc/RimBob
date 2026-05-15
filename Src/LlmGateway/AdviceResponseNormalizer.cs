using System.Text.Json;
using System.Text.Json.Nodes;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;

namespace RimAI.LLM;

internal sealed record LlmAdviceNormalizationContext(
    string Minister,
    string Domain,
    long BriefingVersion,
    long GameTick,
    DateStamp Date,
    string DefaultAdviceType,
    string DefaultRationale,
    IReadOnlyList<GuideCitation> GuideContext);

internal sealed record NormalizedAdviceResponse(
    string? StateSummary,
    IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string? Notes);

internal static class AdviceResponseNormalizer
{
    public static FoodLlmResponse NormalizeStrictResponse(FoodLlmResponse response) =>
        response with
        {
            Advice = NormalizeStrictAdviceItems(response.Advice),
            Flags = NormalizeStrictFlags(response.Flags)
        };

    public static NormalizedAdviceResponse Normalize(
        JsonNode root,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        JsonArray adviceArray = root["advice"]?.AsArray() ?? [];
        JsonArray flagsArray = root["flags"]?.AsArray() ?? [];
        string? stateSummary =
            LlmResponseParser.ReadString(root["state_summary"]) ??
            LlmResponseParser.ReadString(root["current_state"]) ??
            LlmResponseParser.ReadString(root["summary"]);
        string? notes = LlmResponseParser.ReadString(root["notes"]);

        List<AdviceItem> advice = [];
        for (int i = 0; i < adviceArray.Count; i++)
        {
            JsonNode? node = adviceArray[i];
            if (node is null) continue;
            advice.Add(NormalizeAdvice(node, context, notes, i, json));
        }

        List<AgentFlag> flags = [];
        for (int i = 0; i < flagsArray.Count; i++)
        {
            JsonNode? node = flagsArray[i];
            if (node is null) continue;
            AgentFlag? flag = NormalizeFlag(node, context, i, json);
            if (flag is not null) flags.Add(flag);
        }

        return new NormalizedAdviceResponse(stateSummary, advice, flags, notes);
    }

    private static AdviceItem NormalizeAdvice(
        JsonNode node,
        LlmAdviceNormalizationContext context,
        string? notes,
        int index,
        JsonSerializerOptions json)
    {
        AdviceItem? strict = LlmResponseParser.TryDeserialize<AdviceItem>(node, json);
        if (strict is not null && node["priority"] is not null && IsCompleteStrictAdvice(strict))
            return NormalizeStrictAdvice(strict);

        string rawType = LlmResponseParser.ReadString(node["advice_type"]) ?? context.DefaultAdviceType;
        AdvicePriority priority = AdviceJsonCompatibility.ParseAdvicePriority(
            LlmResponseParser.ReadString(node["priority"]) ?? LlmResponseParser.ReadString(node["severity"]),
            node["priority_score"],
            AdvicePriority.Medium);
        string adviceType = LlmResponseParser.ToSnakeCase(rawType);
        if (string.IsNullOrWhiteSpace(adviceType))
            adviceType = LlmResponseParser.ToSnakeCase(context.DefaultAdviceType);
        string title = LlmResponseParser.ReadString(node["title"]) ?? LlmResponseParser.HumanizeIdentifier(rawType);
        string body = LlmResponseParser.ReadString(node["body"]) ?? LlmResponseParser.ReadString(node["message"]) ?? title;
        string rationale = LlmResponseParser.ReadString(node["rationale"]) ?? notes ?? context.DefaultRationale;
        IReadOnlyList<ResourceRequest> requests = ResourceRequestNormalizer.Normalize(node["resource_requests"], priority, context, json);
        IReadOnlyList<SuggestedAction> actions = SuggestedActionNormalizer.Normalize(node["suggested_actions"], json);
        IReadOnlyList<string> citationIds = NormalizeCitationIds(node, context.GuideContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new AdviceItem(
            Id: $"{context.Domain}_llm_{adviceType}_{context.GameTick}_{index + 1}",
            Minister: context.Minister,
            AdviceType: adviceType,
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            ResourceRequests: requests,
            SuggestedActions: actions,
            GuideCitationIds: citationIds,
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedInGameTick: FormatTick(context.Date),
            BriefingRef: new BriefingRef(context.Minister, context.BriefingVersion, $"{context.Domain}:{context.BriefingVersion}")
        );
    }

    private static AgentFlag? NormalizeFlag(
        JsonNode node,
        LlmAdviceNormalizationContext context,
        int index,
        JsonSerializerOptions json)
    {
        AgentFlag? strict = LlmResponseParser.TryDeserialize<AgentFlag>(node, json);
        if (strict is not null && !string.IsNullOrWhiteSpace(strict.Id))
        {
            bool requestsComplete = strict.Requests is null ||
                                    strict.Requests.All(request =>
                                        !string.IsNullOrWhiteSpace(request.What) &&
                                        !string.IsNullOrWhiteSpace(request.Why));
            return requestsComplete
                ? strict
                : strict with
                {
                    Requests = ResourceRequestNormalizer.Normalize(
                        node["requests"],
                        MapFlagSeverityToAdvicePriority(strict.Severity),
                        context,
                        json)
                };
        }

        string? raw = LlmResponseParser.ReadString(node);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = $"{context.Domain}:{LlmResponseParser.ToSnakeCase(raw)}";
        if (id == $"{context.Domain}:") id = $"{context.Domain}:llm_flag_{context.GameTick}_{index + 1}";

        return new AgentFlag(
            Id: id,
            SourceMinister: context.Minister,
            Severity: InferFlagSeverity(raw),
            Domain: context.Domain,
            Summary: LlmResponseParser.HumanizeIdentifier(raw),
            Detail: raw,
            ExpiresAt: now.AddHours(24));
    }

    private static IReadOnlyList<AdviceItem> NormalizeStrictAdviceItems(IReadOnlyList<AdviceItem> advice) =>
        advice.Select(NormalizeStrictAdvice).ToArray();

    private static AdviceItem NormalizeStrictAdvice(AdviceItem advice) =>
        advice with
        {
            ResourceRequests = advice.ResourceRequests.Select(ResourceRequestNormalizer.Normalize).ToArray()
        };

    private static IReadOnlyList<AgentFlag> NormalizeStrictFlags(IReadOnlyList<AgentFlag> flags) =>
        flags.Select(flag => flag with
        {
            Requests = flag.Requests?.Select(ResourceRequestNormalizer.Normalize).ToArray()
        }).ToArray();

    private static bool IsCompleteStrictAdvice(AdviceItem advice) =>
        !string.IsNullOrWhiteSpace(advice.Id) &&
        advice.ResourceRequests.All(request =>
            !string.IsNullOrWhiteSpace(request.What) &&
            !string.IsNullOrWhiteSpace(request.Why)) &&
        advice.SuggestedActions.All(action =>
            !string.IsNullOrWhiteSpace(action.What));

    private static IReadOnlyList<string> NormalizeCitationIds(JsonNode node, IReadOnlyList<GuideCitation> guideContext)
    {
        HashSet<string> allowed = guideContext.Select(c => c.CiteId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JsonArray? array = (node["guide_citations"] ?? node["cite_ids"])?.AsArray();
        if (array is null) return [];

        List<string> ids = [];
        foreach (JsonNode? item in array)
        {
            string? id = LlmResponseParser.ReadString(item);
            if (!string.IsNullOrWhiteSpace(id) && allowed.Contains(id))
                ids.Add(id);
        }

        return ids;
    }

    private static FlagSeverity InferFlagSeverity(string raw)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        if (normalized.Contains("urgent") ||
            normalized.Contains("critical") ||
            normalized.Contains("shortage") ||
            normalized.Contains("nomeals"))
        {
            return FlagSeverity.High;
        }

        return FlagSeverity.Medium;
    }

    private static AdvicePriority MapFlagSeverityToAdvicePriority(FlagSeverity severity) => severity switch
    {
        FlagSeverity.Critical => AdvicePriority.Critical,
        FlagSeverity.High => AdvicePriority.High,
        FlagSeverity.Medium => AdvicePriority.Medium,
        _ => AdvicePriority.Low
    };

    private static string FormatTick(DateStamp date) =>
        $"Y{date.Year ?? 0}{date.Quadrum ?? "?"}D{date.Day ?? 0}";
}
