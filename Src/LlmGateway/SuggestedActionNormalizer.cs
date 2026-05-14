using System.Text.Json;
using System.Text.Json.Nodes;
using RimAI.Core.Advice;

namespace RimAI.LLM;

internal static class SuggestedActionNormalizer
{
    public static IReadOnlyList<SuggestedAction> Normalize(JsonNode? node, JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return [];

        List<SuggestedAction> actions = [];
        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            if (item is not JsonObject)
            {
                string? rawText = LlmResponseParser.ReadString(item);
                if (!string.IsNullOrWhiteSpace(rawText))
                    actions.Add(new SuggestedAction(SuggestedActionKind.Note, rawText));

                continue;
            }

            SuggestedAction? strict = LlmResponseParser.TryDeserialize<SuggestedAction>(item, json);
            if (strict is not null && !string.IsNullOrWhiteSpace(strict.What))
            {
                actions.Add(strict);
                continue;
            }

            string? instruction = LlmResponseParser.ReadString(item["instruction"]) ??
                                  LlmResponseParser.ReadString(item["what"]);
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                actions.Add(new SuggestedAction(
                    ParseKind(LlmResponseParser.ReadString(item["kind"])),
                    instruction));
                continue;
            }

            string? text = LlmResponseParser.ReadString(item);
            if (!string.IsNullOrWhiteSpace(text))
                actions.Add(new SuggestedAction(SuggestedActionKind.Note, text));
        }

        return actions;
    }

    public static SuggestedActionKind ParseKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SuggestedActionKind.Note;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (SuggestedActionKind kind in Enum.GetValues<SuggestedActionKind>())
        {
            if (LlmResponseParser.NormalizeIdentifier(kind.ToString()) == normalized)
                return kind;
        }

        return SuggestedActionKind.Note;
    }
}
