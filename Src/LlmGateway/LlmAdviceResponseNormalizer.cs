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
    IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string? Notes);

internal static class LlmAdviceResponseNormalizer
{
    public static NormalizedAdviceResponse Normalize(
        JsonNode root,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        JsonArray adviceArray = root["advice"]?.AsArray() ?? [];
        JsonArray flagsArray = root["flags"]?.AsArray() ?? [];
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

        return new NormalizedAdviceResponse(advice, flags, notes);
    }

    private static AdviceItem NormalizeAdvice(
        JsonNode node,
        LlmAdviceNormalizationContext context,
        string? notes,
        int index,
        JsonSerializerOptions json)
    {
        AdviceItem? strict = LlmResponseParser.TryDeserialize<AdviceItem>(node, json);
        if (strict is not null && !string.IsNullOrWhiteSpace(strict.Id))
            return strict;

        string rawType = LlmResponseParser.ReadString(node["advice_type"]) ?? context.DefaultAdviceType;
        AdviceSeverity severity = ParseAdviceSeverity(LlmResponseParser.ReadString(node["severity"]), AdviceSeverity.Medium);
        string adviceType = LlmResponseParser.ToSnakeCase(rawType);
        if (string.IsNullOrWhiteSpace(adviceType))
            adviceType = LlmResponseParser.ToSnakeCase(context.DefaultAdviceType);
        string title = LlmResponseParser.ReadString(node["title"]) ?? LlmResponseParser.HumanizeIdentifier(rawType);
        string body = LlmResponseParser.ReadString(node["body"]) ?? LlmResponseParser.ReadString(node["message"]) ?? title;
        string rationale = LlmResponseParser.ReadString(node["rationale"]) ?? notes ?? context.DefaultRationale;
        IReadOnlyList<ResourceRequest> requests = NormalizeResourceRequests(node["resource_requests"], severity, context, json);
        IReadOnlyList<SuggestedAction> actions = NormalizeSuggestedActions(node["suggested_actions"], json);
        IReadOnlyList<string> citationIds = NormalizeCitationIds(node, context.GuideContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new AdviceItem(
            Id: $"{context.Domain}_llm_{adviceType}_{context.GameTick}_{index + 1}",
            Minister: context.Minister,
            AdviceType: adviceType,
            Severity: severity,
            Title: title,
            Body: body,
            Rationale: rationale,
            ResourceRequests: requests,
            SuggestedActions: actions,
            GuideCitationIds: citationIds,
            IssuedAt: now,
            ExpiresAt: now.AddHours(severity >= AdviceSeverity.High ? 4 : 24),
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
            return strict;

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

    private static IReadOnlyList<ResourceRequest> NormalizeResourceRequests(
        JsonNode? node,
        AdviceSeverity severity,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<ResourceRequest> requests = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            ResourceRequest? strict = LlmResponseParser.TryDeserialize<ResourceRequest>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.What))
            {
                requests.Add(strict);
                continue;
            }

            string rawType = LlmResponseParser.ReadString(item["resource_type"]) ??
                             LlmResponseParser.ReadString(item["kind"]) ??
                             LlmResponseParser.ReadString(item["type"]) ??
                             "attention";
            string? amount = LlmResponseParser.ReadNumberAsString(item["amount"]);
            string? unit = LlmResponseParser.ReadString(item["unit"]);
            string description = LlmResponseParser.ReadString(item["description"]) ??
                                 LlmResponseParser.ReadString(item["why"]) ??
                                 $"{context.Minister} LLM requested this resource.";
            ResourceRequestKind kind = ParseResourceKind(rawType, description);
            string what = LlmResponseParser.ReadString(item["what"]) ?? FormatResourceWhat(rawType, amount, unit);
            int? quantity = LlmResponseParser.TryReadIntegerQuantity(item["amount"]);

            requests.Add(new ResourceRequest(
                Kind: kind,
                What: what,
                Why: description,
                Quantity: quantity,
                Priority: severity >= AdviceSeverity.High ? severity : null,
                RequestedFrom: DefaultRequester(kind, context)));
        }

        return requests;
    }

    private static IReadOnlyList<SuggestedAction> NormalizeSuggestedActions(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<SuggestedAction> actions = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            SuggestedAction? strict = LlmResponseParser.TryDeserialize<SuggestedAction>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.What))
            {
                actions.Add(strict);
                continue;
            }

            string? text = LlmResponseParser.ReadString(item);
            if (!string.IsNullOrWhiteSpace(text))
                actions.Add(new SuggestedAction(SuggestedActionKind.Note, text));
        }

        return actions;
    }

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

    private static AdviceSeverity ParseAdviceSeverity(string? raw, AdviceSeverity fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        foreach (AdviceSeverity severity in Enum.GetValues<AdviceSeverity>())
        {
            if (LlmResponseParser.NormalizeIdentifier(severity.ToString()) == LlmResponseParser.NormalizeIdentifier(raw))
                return severity;
        }
        return fallback;
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

    private static ResourceRequestKind ParseResourceKind(string rawType, string description)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {description}");
        if (normalized.Contains("labor") || normalized.Contains("pawnhour")) return ResourceRequestKind.Labor;
        if (normalized.Contains("cooler") || normalized.Contains("building") || normalized.Contains("construction")) return ResourceRequestKind.Building;
        if (normalized.Contains("bill") || normalized.Contains("cook") || normalized.Contains("butcher")) return ResourceRequestKind.Bill;
        if (normalized.Contains("stockpile") || normalized.Contains("zone")) return ResourceRequestKind.StockpileSpace;
        if (normalized.Contains("trade") || normalized.Contains("silver") || normalized.Contains("buy")) return ResourceRequestKind.TradeCapacity;
        if (normalized.Contains("tile")) return ResourceRequestKind.Tile;
        if (normalized.Contains("component") || normalized.Contains("steel") || normalized.Contains("item")) return ResourceRequestKind.Item;
        return ResourceRequestKind.Attention;
    }

    private static string? DefaultRequester(ResourceRequestKind kind, LlmAdviceNormalizationContext context) => kind switch
    {
        ResourceRequestKind.Labor => "Labor",
        ResourceRequestKind.Building => "Construction",
        ResourceRequestKind.Bill => context.Minister,
        ResourceRequestKind.StockpileSpace => context.Minister,
        _ => null
    };

    private static string FormatResourceWhat(string rawType, string? amount, string? unit)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(amount)) parts.Add(amount);
        if (!string.IsNullOrWhiteSpace(unit)) parts.Add(unit);
        parts.Add(LlmResponseParser.HumanizeIdentifier(rawType).ToLowerInvariant());
        return string.Join(" ", parts);
    }

    private static string FormatTick(DateStamp date) =>
        $"Y{date.Year ?? 0}{date.Quadrum ?? "?"}D{date.Day ?? 0}";
}
