using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.LLM;

/// <summary>
/// Assembles prompts for LLM calls. System prompts are read from disk once and cached;
/// user messages are built fresh from briefing + prior agenda + lens hints.
/// </summary>
public sealed class PromptBuilder
{
    private static readonly JsonSerializerOptions UserMessageJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Lazy<string> _mayorSystemPrompt = new(() => LoadPrompt("mayor.system.md"));

    public string MayorSystemPrompt => _mayorSystemPrompt.Value;

    public string BuildMayorUserMessage(
        MayorBriefing         briefing,
        MayorAgenda?          previousAgenda,
        IReadOnlyList<string> lensPrefills)
    {
        MayorPromptPayload payload = new(briefing, previousAgenda, lensPrefills);
        return JsonSerializer.Serialize(payload, UserMessageJson);
    }

    private static string LoadPrompt(string fileName)
    {
        string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                             ?? AppContext.BaseDirectory;
        string path = Path.Combine(assemblyDir, "prompts", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt file not found: {path}", path);
        return File.ReadAllText(path);
    }
}

internal sealed record MayorPromptPayload(
    [property: JsonPropertyName("briefing")]        MayorBriefing         Briefing,
    [property: JsonPropertyName("previous_agenda")] MayorAgenda?          PreviousAgenda,
    [property: JsonPropertyName("lens_prefills")]   IReadOnlyList<string> LensPrefills
);
