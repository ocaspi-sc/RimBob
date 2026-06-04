using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.LLM;

/// <summary>
/// Assembles prompts for LLM calls. System prompts are read from disk once and cached;
/// user messages are built fresh from briefing + agenda directives + retrieved
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
    private static readonly Lazy<string> _foodSystemPrompt = new(() => LoadPrompt("food.system.md"));
    private static readonly Lazy<string> _welfareSystemPrompt = new(() => LoadPrompt("welfare.system.md"));

    public string MayorSystemPrompt => _mayorSystemPrompt.Value;
    public string FoodSystemPrompt => _foodSystemPrompt.Value;
    public string WelfareSystemPrompt => _welfareSystemPrompt.Value;

    public string BuildMayorUserMessage(
        MayorBriefing           briefing,
        IReadOnlyList<string>   agendaDirectives,
        IReadOnlyList<GuideCitation> guideContext,
        IReadOnlyList<AgentFlag>? activeFlags = null)
    {
        IReadOnlyList<GuideContextEntry>? guides = guideContext.Count == 0
            ? null
            : [.. guideContext.Select(c => new GuideContextEntry(c.CiteId, c.Heading, c.SourcePath, c.Snippet))];
        MayorPromptPayload payload = new(briefing, agendaDirectives, guides, activeFlags);
        return JsonSerializer.Serialize(payload, UserMessageJson);
    }

    public string BuildFoodUserMessage(
        FoodBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<GuideCitation> guideContext,
        IReadOnlyList<FoodPromptCropCandidate>? cropCandidates = null)
    {
        IReadOnlyList<GuideContextEntry>? guides = guideContext.Count == 0
            ? null
            : [.. guideContext.Select(c => new GuideContextEntry(c.CiteId, c.Heading, c.SourcePath, c.Snippet))];
        IReadOnlyList<FoodPromptCropCandidate>? candidates = cropCandidates is null || cropCandidates.Count == 0
            ? null
            : cropCandidates;
        FoodPromptPayload payload = new(
            briefing,
            context,
            guides,
            candidates);
        return JsonSerializer.Serialize(payload, UserMessageJson);
    }

    public string BuildWelfareUserMessage(
        WelfareSourceBriefing briefing,
        MinisterBriefingContext context,
        IReadOnlyList<GuideCitation> guideContext)
    {
        IReadOnlyList<GuideContextEntry>? guides = guideContext.Count == 0
            ? null
            : [.. guideContext.Select(c => new GuideContextEntry(c.CiteId, c.Heading, c.SourcePath, c.Snippet))];
        WelfarePromptPayload payload = new(
            briefing,
            context,
            guides);
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
    [property: JsonPropertyName("agenda_directives")] IReadOnlyList<string>         AgendaDirectives,
    [property: JsonPropertyName("guide_context")] IReadOnlyList<GuideContextEntry>? GuideContext,
    [property: JsonPropertyName("active_flags")] IReadOnlyList<AgentFlag>? ActiveFlags
);

internal sealed record FoodPromptPayload(
    [property: JsonPropertyName("briefing")] FoodBriefing Briefing,
    [property: JsonPropertyName("minister_context")] MinisterBriefingContext Context,
    [property: JsonPropertyName("guide_context")] IReadOnlyList<GuideContextEntry>? GuideContext,
    [property: JsonPropertyName("crop_candidates")] IReadOnlyList<FoodPromptCropCandidate>? CropCandidates
);

internal sealed record WelfarePromptPayload(
    [property: JsonPropertyName("briefing")] WelfareSourceBriefing Briefing,
    [property: JsonPropertyName("minister_context")] MinisterBriefingContext Context,
    [property: JsonPropertyName("guide_context")] IReadOnlyList<GuideContextEntry>? GuideContext
);

public sealed record FoodPromptCropCandidate(
    [property: JsonPropertyName("crop_def")] string CropDef,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("tiles")] int Tiles,
    [property: JsonPropertyName("harvest_nutrition")] float HarvestNutrition,
    [property: JsonPropertyName("grow_days")] float GrowDays,
    [property: JsonPropertyName("projected_days_added")] float ProjectedDaysAdded,
    [property: JsonPropertyName("fits_season")] bool FitsSeason,
    [property: JsonPropertyName("days_to_winter_margin")] float? DaysToWinterMargin,
    [property: JsonPropertyName("classification_confidence")] float ClassificationConfidence,
    [property: JsonPropertyName("storage_multiplier")] float StorageMultiplier,
    [property: JsonPropertyName("reason")] string Reason);

internal sealed record GuideContextEntry(
    [property: JsonPropertyName("cite_id")] string CiteId,
    [property: JsonPropertyName("heading")] string Heading,
    [property: JsonPropertyName("source")]  string Source,
    [property: JsonPropertyName("snippet")] string Snippet
);
