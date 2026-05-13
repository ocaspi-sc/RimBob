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
            return NormalizeStrictAdvice(strict);

        string rawType = LlmResponseParser.ReadString(node["advice_type"]) ?? context.DefaultAdviceType;
        AdviceSeverity severity = ParseAdviceSeverity(LlmResponseParser.ReadString(node["severity"]), AdviceSeverity.Medium);
        int priorityScore = ParsePriorityScore(node["priority_score"], severity);
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
            PriorityScore: priorityScore,
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
                requests.Add(NormalizeResourceRequest(strict));
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
            WorkType? workType = ParseWorkType(LlmResponseParser.ReadString(item["work_type"])) ??
                                 InferWorkType(rawType, what, description);
            string? skill = LlmResponseParser.ReadString(item["skill"]) ?? DefaultSkill(workType);
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
                Priority: severity >= AdviceSeverity.High ? severity : null,
                RequestedFrom: DefaultRequester(kind, context),
                WorkType: workType,
                Skill: skill));
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

    private static int ParsePriorityScore(JsonNode? node, AdviceSeverity severity)
    {
        int? value = LlmResponseParser.TryReadIntegerQuantity(node);
        if (value is null) return AdvicePriorityScore.DefaultForSeverity(severity);
        return AdvicePriorityScore.Normalize(value.Value, severity);
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

    private static IReadOnlyList<AdviceItem> NormalizeStrictAdviceItems(IReadOnlyList<AdviceItem> advice) =>
        advice.Select(NormalizeStrictAdvice).ToArray();

    private static AdviceItem NormalizeStrictAdvice(AdviceItem advice) =>
        advice with
        {
            PriorityScore = AdvicePriorityScore.Normalize(advice.PriorityScore, advice.Severity),
            ResourceRequests = advice.ResourceRequests.Select(NormalizeResourceRequest).ToArray()
        };

    private static IReadOnlyList<AgentFlag> NormalizeStrictFlags(IReadOnlyList<AgentFlag> flags) =>
        flags.Select(flag => flag with
        {
            Requests = flag.Requests?.Select(NormalizeResourceRequest).ToArray()
        }).ToArray();

    private static ResourceRequest NormalizeResourceRequest(ResourceRequest request)
    {
        WorkType? workType = request.WorkType ?? InferWorkType(request.Kind.ToString(), request.What, request.Why);
        string? skill = string.IsNullOrWhiteSpace(request.Skill) ? DefaultSkill(workType) : request.Skill;
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

    private static WorkType? ParseWorkType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (WorkType workType in Enum.GetValues<WorkType>())
        {
            if (LlmResponseParser.NormalizeIdentifier(workType.ToString()) == normalized)
                return workType;
        }

        return AliasWorkType(normalized);
    }

    private static WorkType? InferWorkType(params string?[] values)
    {
        string joined = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        string normalized = LlmResponseParser.NormalizeIdentifier(joined);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return AliasWorkType(normalized);
    }

    private static WorkType? AliasWorkType(string normalized)
    {
        if (normalized.Contains("firefight")) return WorkType.Firefight;
        if (normalized.Contains("patient")) return WorkType.Patient;
        if (normalized.Contains("doctor") || normalized.Contains("tend") || normalized.Contains("medicine")) return WorkType.Doctor;
        if (normalized.Contains("bedrest")) return WorkType.BedRest;
        if (normalized.Contains("warden")) return WorkType.Warden;
        if (normalized.Contains("handle") || normalized.Contains("animal")) return WorkType.Handle;
        if (normalized.Contains("cook") || normalized.Contains("meal") || normalized.Contains("stove")) return WorkType.Cook;
        if (normalized.Contains("hunt")) return WorkType.Hunt;
        if (normalized.Contains("construct") || normalized.Contains("build") || normalized.Contains("repair")) return WorkType.Construct;
        if (normalized.Contains("grow") || normalized.Contains("sow") || normalized.Contains("farm")) return WorkType.Grow;
        if (normalized.Contains("mine")) return WorkType.Mine;
        if (normalized.Contains("plantcut") || normalized.Contains("harvest") || normalized.Contains("forage") || normalized.Contains("berry")) return WorkType.PlantCut;
        if (normalized.Contains("smith")) return WorkType.Smith;
        if (normalized.Contains("tailor")) return WorkType.Tailor;
        if (normalized.Contains("art") || normalized.Contains("sculpt")) return WorkType.Art;
        if (normalized.Contains("craft")) return WorkType.Craft;
        if (normalized.Contains("haul")) return WorkType.Haul;
        if (normalized.Contains("clean")) return WorkType.Clean;
        if (normalized.Contains("research")) return WorkType.Research;
        if (normalized.Contains("basic")) return WorkType.Basic;
        return null;
    }

    private static string? DefaultSkill(WorkType? workType) => workType switch
    {
        WorkType.Doctor => "Medicine",
        WorkType.Warden => "Social",
        WorkType.Handle => "Animals",
        WorkType.Cook => "Cooking",
        WorkType.Hunt => "Shooting",
        WorkType.Construct => "Construction",
        WorkType.Grow => "Plants",
        WorkType.Mine => "Mining",
        WorkType.PlantCut => "Plants",
        WorkType.Smith => "Crafting",
        WorkType.Tailor => "Crafting",
        WorkType.Art => "Artistic",
        WorkType.Craft => "Crafting",
        WorkType.Research => "Intellectual",
        _ => null
    };

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
