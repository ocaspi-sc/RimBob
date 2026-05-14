using System.Text.Json;
using System.Text.Json.Nodes;
using RimAI.Core.Advice;

namespace RimAI.LLM;

internal static class ResourceRequestNormalizer
{
    public static IReadOnlyList<ResourceRequest> Normalize(
        JsonNode? node,
        AdvicePriority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<ResourceRequest> requests = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            if (item is not JsonObject)
            {
                string? text = LlmResponseParser.ReadString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    requests.Add(new ResourceRequest(
                        ResourceRequestKind.Attention,
                        text,
                        $"{context.Minister} LLM requested this resource.",
                        Priority: priority >= AdvicePriority.High ? priority : null));
                }

                continue;
            }

            ResourceRequest? strict = LlmResponseParser.TryDeserialize<ResourceRequest>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.What))
            {
                requests.Add(Normalize(strict));
                continue;
            }

            string rawType = LlmResponseParser.ReadString(item["resource_type"]) ??
                             LlmResponseParser.ReadString(item["kind"]) ??
                             LlmResponseParser.ReadString(item["type"]) ??
                             "attention";
            string? amount = LlmResponseParser.ReadNumberAsString(item["amount"]);
            string? unit = LlmResponseParser.ReadString(item["unit"]);
            string description = LlmResponseParser.ReadString(item["reason"]) ??
                                 LlmResponseParser.ReadString(item["description"]) ??
                                 LlmResponseParser.ReadString(item["why"]) ??
                                 $"{context.Minister} LLM requested this resource.";
            ResourceRequestKind kind = ParseResourceKind(rawType, description);
            string what = LlmResponseParser.ReadString(item["request"]) ??
                          LlmResponseParser.ReadString(item["what"]) ??
                          FormatResourceWhat(rawType, amount, unit);
            int? quantity = LlmResponseParser.TryReadIntegerQuantity(item["amount"]);
            WorkType? workType = WorkTypeInference.Parse(LlmResponseParser.ReadString(item["work_type"])) ??
                                 WorkTypeInference.Infer(rawType, what, description);
            string? skill = LlmResponseParser.ReadString(item["skill"]) ?? WorkTypeInference.DefaultSkill(workType);
            if (kind == ResourceRequestKind.Labor && workType is null)
            {
                kind = ResourceRequestKind.Attention;
                description = $"{description} Normalized as attention because the labor request did not name a RimWorld work type.";
            }

            requests.Add(new ResourceRequest(
                Kind: kind,
                What: what,
                Why: description,
                Quantity: quantity,
                Priority: priority >= AdvicePriority.High ? priority : null,
                RequestedFrom: DefaultRequester(kind, context),
                WorkType: workType,
                Skill: skill));
        }

        return requests;
    }

    public static ResourceRequest Normalize(ResourceRequest request)
    {
        WorkType? workType = request.WorkType ?? WorkTypeInference.Infer(request.Kind.ToString(), request.What, request.Why);
        string? skill = string.IsNullOrWhiteSpace(request.Skill)
            ? WorkTypeInference.DefaultSkill(workType)
            : request.Skill;
        ResourceRequestKind kind = request.Kind;
        string why = request.Why;

        if (kind == ResourceRequestKind.Labor && workType is null)
        {
            kind = ResourceRequestKind.Attention;
            why = $"{why} Normalized as attention because the labor request did not name a RimWorld work type.";
        }

        return request with
        {
            Kind = kind,
            Why = why,
            WorkType = workType,
            Skill = skill
        };
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
}
