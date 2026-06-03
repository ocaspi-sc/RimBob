using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

public sealed record FoodLlmResponse
{
    [JsonConstructor]
    public FoodLlmResponse(
        string? StateSummary,
        IReadOnlyList<AdviceItem> Advice,
        IReadOnlyList<RimBob.Core.Ministers.AgentFlag> Flags,
        string? Notes = null)
    {
        this.StateSummary = StateSummary;
        this.Advice = Advice;
        this.Flags = Flags;
        this.Notes = Notes;
    }

    public FoodLlmResponse(
        IReadOnlyList<AdviceItem> Advice,
        IReadOnlyList<RimBob.Core.Ministers.AgentFlag> Flags,
        string? Notes = null)
        : this(null, Advice, Flags, Notes)
    {
    }

    [JsonPropertyName("state_summary")]
    public string? StateSummary { get; init; }

    [JsonPropertyName("advice")]
    public IReadOnlyList<AdviceItem> Advice { get; init; }

    [JsonPropertyName("flags")]
    public IReadOnlyList<RimBob.Core.Ministers.AgentFlag> Flags { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
