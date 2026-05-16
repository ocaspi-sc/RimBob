using System.Text.Json.Nodes;
using RimBob.Core.Advice;

namespace RimBob.LLM;

internal static class AdviceJsonCompatibility
{
    public static AdvicePriority ParseAdvicePriority(
        string? raw,
        JsonNode? legacyPriorityScore,
        AdvicePriority fallback)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string normalized = LlmResponseParser.NormalizeIdentifier(raw);
            foreach (AdvicePriority priority in Enum.GetValues<AdvicePriority>())
            {
                if (LlmResponseParser.NormalizeIdentifier(priority.ToString()) == normalized)
                    return priority;
            }
        }

        int? score = LlmResponseParser.TryReadIntegerQuantity(legacyPriorityScore);
        if (score is >= 10) return AdvicePriority.Critical;
        if (score is >= 8) return AdvicePriority.High;
        if (score is >= 5) return AdvicePriority.Medium;
        if (score is >= 1) return AdvicePriority.Low;
        return fallback;
    }
}
