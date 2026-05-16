using System.Text.RegularExpressions;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.LLM;

public static class AdviceTextStyleWarnings
{
    private const int MaxTitleWords = 7;
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

            for (int i = 0; i < advice.Steps.Count; i++)
            {
                AdviceStep step = advice.Steps[i];
                string instructionField = $"{adviceId}.steps[{i}].instruction";
                AddWordWarning(warnings, instructionField, step.Instruction, MaxInstructionWords, "use one short imperative sentence");

                if (!string.IsNullOrWhiteSpace(step.Reason))
                    AddWordWarning(warnings, $"{adviceId}.steps[{i}].reason", step.Reason, MaxReasonWords, "use one short cause");

                if (SentenceCount(step.Instruction) > 1)
                    warnings.Add($"{instructionField} has multiple sentences; use one short imperative sentence.");
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
