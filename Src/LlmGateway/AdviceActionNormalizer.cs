using System.Text.Json;
using System.Text.Json.Nodes;
using RimBob.Core.Advice;

namespace RimBob.LLM;

internal static class AdviceActionNormalizer
{
    public static IReadOnlyList<AdviceAction> NormalizeOrConvertLegacy(
        JsonNode? actionsNode,
        JsonNode? legacyStepsNode,
        JsonNode? resourceRequestsNode,
        JsonNode? suggestedActionsNode,
        AdvicePriority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        IReadOnlyList<AdviceAction> actions = Normalize(actionsNode, json);
        if (actions.Count > 0) return actions;

        IReadOnlyList<AdviceAction> legacySteps = Normalize(legacyStepsNode, json);
        if (legacySteps.Count > 0) return legacySteps;

        return ConvertLegacy(resourceRequestsNode, suggestedActionsNode, priority, context, json);
    }

    public static IReadOnlyList<AdviceAction> Normalize(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<AdviceAction> actions = [];
        foreach (JsonNode? item in array)
        {
            AdviceAction? action = NormalizeItem(item, json);
            if (action is not null) actions.Add(action);
        }

        return actions;
    }

    public static AdviceAction Normalize(AdviceAction action)
    {
        WorkType? workType = action.WorkType ??
                             (ShouldInferWorkType(action.Kind)
                                 ? WorkTypeInference.Infer(action.Kind.ToString(), action.Instruction)
                                 : null);
        string? skill = string.IsNullOrWhiteSpace(action.Skill)
            ? WorkTypeInference.DefaultSkill(workType)
            : action.Skill;

        return action with
        {
            Instruction = action.Instruction.Trim(),
            Owner = string.IsNullOrWhiteSpace(action.Owner) ? null : action.Owner.Trim(),
            WorkType = workType,
            Skill = skill,
            Apply = null
        };
    }

    private static bool ShouldInferWorkType(AdviceActionKind kind) => kind is
        AdviceActionKind.DesignateZone or
        AdviceActionKind.MarkHarvest or
        AdviceActionKind.MarkHunt or
        AdviceActionKind.ProductionBill or
        AdviceActionKind.SetPriority or
        AdviceActionKind.RequestResource;

    private static IReadOnlyList<AdviceAction> ConvertLegacy(
        JsonNode? resourceRequestsNode,
        JsonNode? suggestedActionsNode,
        AdvicePriority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        List<AdviceAction> actions = [];

        foreach (ResourceRequest request in ResourceRequestNormalizer.Normalize(resourceRequestsNode, priority, context, json))
        {
            actions.Add(Normalize(new AdviceAction(
                Kind: AdviceActionKind.RequestResource,
                Instruction: request.What,
                Quantity: request.Quantity,
                Owner: request.RequestedFrom,
                WorkType: request.WorkType,
                Skill: request.Skill)));
        }

        foreach (AdviceAction action in NormalizeLegacyActions(suggestedActionsNode, json))
        {
            actions.Add(action);
        }

        return actions;
    }

    private static IReadOnlyList<AdviceAction> NormalizeLegacyActions(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<AdviceAction> actions = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            if (item is not JsonObject)
            {
                string? rawText = LlmResponseParser.ReadString(item);
                if (!string.IsNullOrWhiteSpace(rawText))
                    actions.Add(new AdviceAction(AdviceActionKind.Note, rawText.Trim()));

                continue;
            }

            AdviceAction? strict = LlmResponseParser.TryDeserialize<AdviceAction>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.Instruction))
            {
                actions.Add(Normalize(strict));
                continue;
            }

            string? instruction = LlmResponseParser.ReadString(item["instruction"]) ??
                                  LlmResponseParser.ReadString(item["what"]);
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                actions.Add(Normalize(new AdviceAction(
                    ParseKind(LlmResponseParser.ReadString(item["kind"])),
                    instruction)));
                continue;
            }

            string? text = LlmResponseParser.ReadString(item);
            if (!string.IsNullOrWhiteSpace(text))
                actions.Add(new AdviceAction(AdviceActionKind.Note, text.Trim()));
        }

        return actions;
    }

    private static AdviceAction? NormalizeItem(JsonNode? item, JsonSerializerOptions json)
    {
        if (item is null) return null;
        if (item is not JsonObject)
        {
            string? rawText = LlmResponseParser.ReadString(item);
            return string.IsNullOrWhiteSpace(rawText)
                ? null
                : new AdviceAction(AdviceActionKind.Note, rawText.Trim());
        }

        AdviceAction? strict = LlmResponseParser.TryDeserialize<AdviceAction>(item, json);
        if (strict is not null && !string.IsNullOrWhiteSpace(strict.Instruction))
            return Normalize(strict);

        string? instruction = LlmResponseParser.ReadString(item["instruction"]) ??
                              LlmResponseParser.ReadString(item["what"]) ??
                              LlmResponseParser.ReadString(item["request"]);
        if (string.IsNullOrWhiteSpace(instruction)) return null;

        WorkType? workType = WorkTypeInference.Parse(LlmResponseParser.ReadString(item["work_type"]));
        return Normalize(new AdviceAction(
            Kind: ParseKind(LlmResponseParser.ReadString(item["kind"])),
            Instruction: instruction,
            Quantity: LlmResponseParser.TryReadIntegerQuantity(item["quantity"] ?? item["amount"]),
            Owner: LlmResponseParser.ReadString(item["owner"]) ?? LlmResponseParser.ReadString(item["requested_from"]),
            WorkType: workType,
            Skill: LlmResponseParser.ReadString(item["skill"])));
    }

    private static AdviceActionKind ParseKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AdviceActionKind.Note;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (AdviceActionKind kind in Enum.GetValues<AdviceActionKind>())
        {
            if (LlmResponseParser.NormalizeIdentifier(kind.ToString()) == normalized)
                return kind;
        }

        return AdviceActionKind.Note;
    }
}
