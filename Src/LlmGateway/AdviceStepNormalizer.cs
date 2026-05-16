using System.Text.Json;
using System.Text.Json.Nodes;
using RimAI.Core.Advice;

namespace RimAI.LLM;

internal static class AdviceStepNormalizer
{
    public static IReadOnlyList<AdviceStep> NormalizeOrConvertLegacy(
        JsonNode? stepsNode,
        JsonNode? resourceRequestsNode,
        JsonNode? suggestedActionsNode,
        AdvicePriority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        IReadOnlyList<AdviceStep> steps = Normalize(stepsNode, json);
        if (steps.Count > 0) return steps;

        return ConvertLegacy(resourceRequestsNode, suggestedActionsNode, priority, context, json);
    }

    public static IReadOnlyList<AdviceStep> Normalize(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<AdviceStep> steps = [];
        foreach (JsonNode? item in array)
        {
            AdviceStep? step = NormalizeItem(item, json);
            if (step is not null) steps.Add(step);
        }

        return steps;
    }

    public static AdviceStep Normalize(AdviceStep step)
    {
        WorkType? workType = step.WorkType ??
                             (ShouldInferWorkType(step.Kind)
                                 ? WorkTypeInference.Infer(step.Kind.ToString(), step.Instruction, step.Reason)
                                 : null);
        string? skill = string.IsNullOrWhiteSpace(step.Skill)
            ? WorkTypeInference.DefaultSkill(workType)
            : step.Skill;

        return step with
        {
            Instruction = step.Instruction.Trim(),
            Owner = string.IsNullOrWhiteSpace(step.Owner) ? null : step.Owner.Trim(),
            WorkType = workType,
            Skill = skill,
            Reason = string.IsNullOrWhiteSpace(step.Reason) ? null : step.Reason.Trim()
        };
    }

    private static bool ShouldInferWorkType(AdviceStepKind kind) => kind is
        AdviceStepKind.DesignateZone or
        AdviceStepKind.MarkHarvest or
        AdviceStepKind.MarkHunt or
        AdviceStepKind.ProductionBill or
        AdviceStepKind.SetPriority or
        AdviceStepKind.RequestResource;

    private static IReadOnlyList<AdviceStep> ConvertLegacy(
        JsonNode? resourceRequestsNode,
        JsonNode? suggestedActionsNode,
        AdvicePriority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        List<AdviceStep> steps = [];

        foreach (ResourceRequest request in ResourceRequestNormalizer.Normalize(resourceRequestsNode, priority, context, json))
        {
            steps.Add(Normalize(new AdviceStep(
                Kind: AdviceStepKind.RequestResource,
                Instruction: request.What,
                Quantity: request.Quantity,
                Owner: request.RequestedFrom,
                WorkType: request.WorkType,
                Skill: request.Skill,
                Reason: request.Why,
                Icon: request.Icon)));
        }

        foreach (AdviceStep step in NormalizeLegacyActions(suggestedActionsNode, json))
        {
            steps.Add(step);
        }

        return steps;
    }

    private static IReadOnlyList<AdviceStep> NormalizeLegacyActions(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<AdviceStep> steps = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            if (item is not JsonObject)
            {
                string? rawText = LlmResponseParser.ReadString(item);
                if (!string.IsNullOrWhiteSpace(rawText))
                    steps.Add(new AdviceStep(AdviceStepKind.Note, rawText.Trim()));

                continue;
            }

            AdviceStep? strict = LlmResponseParser.TryDeserialize<AdviceStep>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.Instruction))
            {
                steps.Add(Normalize(strict));
                continue;
            }

            string? instruction = LlmResponseParser.ReadString(item["instruction"]) ??
                                  LlmResponseParser.ReadString(item["what"]);
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                steps.Add(Normalize(new AdviceStep(
                    ParseKind(LlmResponseParser.ReadString(item["kind"])),
                    instruction,
                    Icon: ParseIcon(item["icon"], json))));
                continue;
            }

            string? text = LlmResponseParser.ReadString(item);
            if (!string.IsNullOrWhiteSpace(text))
                steps.Add(new AdviceStep(AdviceStepKind.Note, text.Trim()));
        }

        return steps;
    }

    private static AdviceStep? NormalizeItem(JsonNode? item, JsonSerializerOptions json)
    {
        if (item is null) return null;
        if (item is not JsonObject)
        {
            string? rawText = LlmResponseParser.ReadString(item);
            return string.IsNullOrWhiteSpace(rawText)
                ? null
                : new AdviceStep(AdviceStepKind.Note, rawText.Trim());
        }

        AdviceStep? strict = LlmResponseParser.TryDeserialize<AdviceStep>(item, json);
        if (strict is not null && !string.IsNullOrWhiteSpace(strict.Instruction))
            return Normalize(strict);

        string? instruction = LlmResponseParser.ReadString(item["instruction"]) ??
                              LlmResponseParser.ReadString(item["what"]) ??
                              LlmResponseParser.ReadString(item["request"]);
        if (string.IsNullOrWhiteSpace(instruction)) return null;

        WorkType? workType = WorkTypeInference.Parse(LlmResponseParser.ReadString(item["work_type"]));
        return Normalize(new AdviceStep(
            Kind: ParseKind(LlmResponseParser.ReadString(item["kind"])),
            Instruction: instruction,
            Quantity: LlmResponseParser.TryReadIntegerQuantity(item["quantity"] ?? item["amount"]),
            Owner: LlmResponseParser.ReadString(item["owner"]) ?? LlmResponseParser.ReadString(item["requested_from"]),
            WorkType: workType,
            Skill: LlmResponseParser.ReadString(item["skill"]),
            Reason: LlmResponseParser.ReadString(item["reason"]) ?? LlmResponseParser.ReadString(item["why"]),
            Icon: ParseIcon(item["icon"], json)));
    }

    private static IconRef? ParseIcon(JsonNode? node, JsonSerializerOptions json)
    {
        if (node is null) return null;
        return LlmResponseParser.TryDeserialize<IconRef>(node, json);
    }

    private static AdviceStepKind ParseKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AdviceStepKind.Note;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (AdviceStepKind kind in Enum.GetValues<AdviceStepKind>())
        {
            if (LlmResponseParser.NormalizeIdentifier(kind.ToString()) == normalized)
                return kind;
        }

        return AdviceStepKind.Note;
    }
}
