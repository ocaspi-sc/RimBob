using System.Text.RegularExpressions;
using RimAI.Core.Advice;
using RimAI.Core.Ministers;

namespace RimAI.LLM;

public static class AdviceTextStyleWarnings
{
    private const int MaxTitleWords = 7;
    private const int MaxRequestWords = 9;
    private const int MaxReasonWords = 12;
    private const int MaxInstructionWords = 14;
    private const int MaxFlagSummaryWords = 14;

    public static IReadOnlyList<string> ForFood(FoodLlmResponse response)
    {
        List<string> warnings = [];

        foreach (AdviceItem advice in response.Advice)
        {
            string adviceId = string.IsNullOrWhiteSpace(advice.Id) ? "advice" : advice.Id;
            AddWordWarning(warnings, $"{adviceId}.title", advice.Title, MaxTitleWords, "keep titles to 3-7 words");

            for (int i = 0; i < advice.ResourceRequests.Count; i++)
            {
                ResourceRequest request = advice.ResourceRequests[i];
                AddWordWarning(warnings, $"{adviceId}.resource_requests[{i}].request", request.What, MaxRequestWords, "use a short noun phrase");
                AddWordWarning(warnings, $"{adviceId}.resource_requests[{i}].reason", request.Why, MaxReasonWords, "use one short cause");
            }

            for (int i = 0; i < advice.SuggestedActions.Count; i++)
            {
                SuggestedAction action = advice.SuggestedActions[i];
                string field = $"{adviceId}.suggested_actions[{i}].instruction";
                AddWordWarning(warnings, field, action.What, MaxInstructionWords, "use one short imperative sentence");

                if (SentenceCount(action.What) > 1)
                    warnings.Add($"{field} has multiple sentences; use one short imperative sentence.");
            }
        }

        for (int i = 0; i < response.Flags.Count; i++)
        {
            AgentFlag flag = response.Flags[i];
            string flagId = string.IsNullOrWhiteSpace(flag.Id) ? $"flags[{i}]" : flag.Id;
            AddWordWarning(warnings, $"{flagId}.summary", flag.Summary, MaxFlagSummaryWords, "use a terse operational signal");
        }

        return warnings;
    }

    private static void AddWordWarning(
        List<string> warnings,
        string field,
        string value,
        int maxWords,
        string guidance)
    {
        int words = WordCount(value);
        if (words > maxWords)
            warnings.Add($"{field} has {words} words; {guidance}.");
    }

    private static int WordCount(string value) =>
        Regex.Matches(value, @"[\p{L}\p{N}]+").Count;

    private static int SentenceCount(string value) =>
        Regex.Matches(value, @"[.!?]+(?:\s|$)").Count;
}
