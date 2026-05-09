using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

namespace RimAI.LLM;

/// <summary>
/// Assembles prompts for LLM calls. System prompts are read from disk once and cached;
/// user messages are built fresh from briefing + prior agenda + agenda directives + retrieved
/// guide passages (M2 RAG).
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
        MayorBriefing           briefing,
        MayorAgenda?            previousAgenda,
        IReadOnlyList<string>   agendaDirectives,
        IReadOnlyList<Citation> retrievedGuides)
    {
        IReadOnlyList<RetrievedGuide>? guides = retrievedGuides.Count == 0
            ? null
            : [.. retrievedGuides.Select(c => new RetrievedGuide(c.CiteId, c.Heading, c.SourcePath, c.Snippet))];
        MayorPromptPayload payload = new(briefing, previousAgenda, agendaDirectives, guides);
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
    [property: JsonPropertyName("briefing")]         MayorBriefing                  Briefing,
    [property: JsonPropertyName("previous_agenda")]  MayorAgenda?                   PreviousAgenda,
    [property: JsonPropertyName("agenda_directives")] IReadOnlyList<string>         AgendaDirectives,
    [property: JsonPropertyName("retrieved_guides")] IReadOnlyList<RetrievedGuide>? RetrievedGuides
);

internal sealed record RetrievedGuide(
    [property: JsonPropertyName("cite_id")] string CiteId,
    [property: JsonPropertyName("heading")] string Heading,
    [property: JsonPropertyName("source")]  string Source,
    [property: JsonPropertyName("snippet")] string Snippet
);
