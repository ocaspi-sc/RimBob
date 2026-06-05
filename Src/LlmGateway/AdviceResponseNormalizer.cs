using System.Text.Json;
using System.Text.Json.Nodes;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.LLM;

internal sealed record LlmAdviceNormalizationContext(
    string Minister,
    string Domain,
    long BriefingVersion,
    long GameTick,
    GameDate? Date,
    string DefaultRationale,
    IReadOnlyList<GuideCitation> GuideContext);

internal sealed record NormalizedAdviceResponse(
    string? StateSummary,
    IReadOnlyList<AdviceItem> Advice,
    IReadOnlyList<AgentFlag> Flags,
    string? Notes,
    int DroppedFlagCount);

internal sealed record NormalizedFlagResult(AgentFlag? Flag, bool Dropped);

internal static class AdviceResponseNormalizer
{
    public static FoodLlmResponse NormalizeStrictResponse(FoodLlmResponse response) =>
        response with
        {
            Advice = NormalizeStrictAdviceItems(response.Advice),
            Flags = NormalizeStrictFlags(response.Flags)
        };

    public static WelfareLlmResponse NormalizeStrictResponse(WelfareLlmResponse response) =>
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
        int droppedFlagCount = 0;
        for (int i = 0; i < flagsArray.Count; i++)
        {
            JsonNode? node = flagsArray[i];
            if (node is null) continue;
            NormalizedFlagResult result = NormalizeFlag(node, context, i, json);
            if (result.Flag is not null) flags.Add(result.Flag);
            if (result.Dropped) droppedFlagCount++;
        }

        return new NormalizedAdviceResponse(stateSummary, advice, flags, notes, droppedFlagCount);
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

        Priority priority = AdviceJsonCompatibility.ParsePriority(
            LlmResponseParser.ReadString(node["priority"]),
            node["priority_score"],
            Priority.Medium);
        IReadOnlyList<AdviceAction> actions = AdviceActionNormalizer.NormalizeOrConvertLegacy(
            node["actions"] ?? node["Actions"],
            node["steps"] ?? node["Steps"],
            node["resource_requests"],
            node["suggested_actions"],
            priority,
            context,
            json);
        string title = LlmResponseParser.ReadString(node["title"]) ?? TitleFromFirstAction(actions, context.Minister);
        string body = LlmResponseParser.ReadString(node["body"]) ?? LlmResponseParser.ReadString(node["message"]) ?? title;
        string rationale = LlmResponseParser.ReadString(node["rationale"]) ?? notes ?? context.DefaultRationale;
        IReadOnlyList<string> citationIds = NormalizeCitationIds(node, context.GuideContext);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new AdviceItem(
            Id: $"{context.Domain}_llm_{context.GameTick}_{index + 1}",
            Minister: context.Minister,
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            Actions: actions,
            GuideCitationIds: citationIds,
            Stamp: new AdviceStamp(
                now,
                now.AddHours(priority >= Priority.High ? 4 : 24),
                context.Date,
                context.GameTick,
                AdviceFreshness.ExpiresGameTick(context.GameTick, priority)),
            BriefingRef: new BriefingRef(context.Minister, context.BriefingVersion, $"{context.Domain}:{context.BriefingVersion}")
        );
    }

    private static string TitleFromFirstAction(IReadOnlyList<AdviceAction> actions, string minister)
    {
        string? firstInstruction = actions
            .Select(action => action.Instruction)
            .FirstOrDefault(instruction => !string.IsNullOrWhiteSpace(instruction));
        return string.IsNullOrWhiteSpace(firstInstruction)
            ? $"{minister} advice"
            : firstInstruction.Trim();
    }

    private static NormalizedFlagResult NormalizeFlag(
        JsonNode node,
        LlmAdviceNormalizationContext context,
        int index,
        JsonSerializerOptions json)
    {
        AgentFlag? strict = TryDeserializeFlag(node, json);
        if (strict is not null && HasRequiredFlagEnvelope(node, strict))
        {
            NormalizedFlagRequests requests = ResourceRequestNormalizer.NormalizeFlagRequests(
                node,
                strict.Priority,
                context,
                json);
            AgentFlag normalized = strict with
            {
                BuildingRequests = requests.BuildingRequestsOrNull,
                LaborRequests = requests.LaborRequestsOrNull,
                ItemRequests = requests.ItemRequestsOrNull,
                ZoneRequests = requests.ZoneRequestsOrNull,
                Attention = requests.AttentionOrNull
            };
            return new NormalizedFlagResult(normalized, Dropped: false);
        }

        if (IsNonEmptyObject(node))
            return new NormalizedFlagResult(null, Dropped: true);

        string? raw = LlmResponseParser.ReadString(node);
        if (string.IsNullOrWhiteSpace(raw))
            return new NormalizedFlagResult(null, Dropped: false);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = $"{context.Domain}:{LlmResponseParser.ToSnakeCase(raw)}";
        if (id == $"{context.Domain}:") id = $"{context.Domain}:llm_flag_{context.GameTick}_{index + 1}";

        return new NormalizedFlagResult(new AgentFlag(
            Id: id,
            SourceMinister: context.Minister,
            Priority: InferFlagPriority(raw),
            Domain: context.Domain,
            Summary: LlmResponseParser.HumanizeIdentifier(raw),
            Detail: raw,
            ExpiresAt: now.AddHours(24)), Dropped: false);
    }

    private static AgentFlag? TryDeserializeFlag(JsonNode node, JsonSerializerOptions json)
    {
        try
        {
            return node.Deserialize<AgentFlag>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasRequiredFlagEnvelope(JsonNode node, AgentFlag flag) =>
        !string.IsNullOrWhiteSpace(flag.Id) &&
        node["priority"] is not null &&
        !string.IsNullOrWhiteSpace(flag.Domain) &&
        node["domain"] is not null &&
        !string.IsNullOrWhiteSpace(flag.Summary) &&
        node["summary"] is not null;

    private static bool IsNonEmptyObject(JsonNode node) =>
        node is JsonObject obj && obj.Count > 0;

    private static IReadOnlyList<AdviceItem> NormalizeStrictAdviceItems(IReadOnlyList<AdviceItem> advice) =>
        advice.Select(NormalizeStrictAdvice).ToArray();

    private static AdviceItem NormalizeStrictAdvice(AdviceItem advice) =>
        advice with
        {
            Actions = advice.Actions.Select(AdviceActionNormalizer.Normalize).ToArray()
        };

    private static IReadOnlyList<AgentFlag> NormalizeStrictFlags(IReadOnlyList<AgentFlag> flags) =>
        flags.Select(ResourceRequestNormalizer.Normalize).ToArray();

    private static bool IsCompleteStrictAdvice(AdviceItem advice) =>
        !string.IsNullOrWhiteSpace(advice.Id) &&
        advice.Stamp is not null &&
        advice.Actions.All(action =>
            !string.IsNullOrWhiteSpace(action.Instruction));

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

    private static Priority InferFlagPriority(string raw)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        if (normalized.Contains("urgent") ||
            normalized.Contains("critical") ||
            normalized.Contains("shortage") ||
            normalized.Contains("nomeals"))
        {
            return Priority.High;
        }

        return Priority.Medium;
    }
}
