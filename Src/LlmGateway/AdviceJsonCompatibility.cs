using System.Text.Json.Nodes;
using RimBob.Core.Advice;

namespace RimBob.LLM;

internal static class AdviceJsonCompatibility
{
    public static Priority ParsePriority(
        string? raw,
        JsonNode? legacyPriorityScore,
        Priority fallback)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string normalized = LlmResponseParser.NormalizeIdentifier(raw);
            foreach (Priority priority in Enum.GetValues<Priority>())
            {
                if (LlmResponseParser.NormalizeIdentifier(priority.ToString()) == normalized)
                    return priority;
            }
        }

        int? score = LlmResponseParser.TryReadIntegerQuantity(legacyPriorityScore);
        if (score is >= 10) return Priority.Critical;
        if (score is >= 8) return Priority.High;
        if (score is >= 5) return Priority.Medium;
        if (score is >= 1) return Priority.Low;
        return fallback;
    }
}
