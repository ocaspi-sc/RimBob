using System.Text.Json.Serialization;

namespace RimAI.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<FoodAdviceType>))]
public enum FoodAdviceType
{
    FoodSecurity,
    ExpandGrowingCapacity,
    HarvestNow,
    WildHarvest,
    HuntForFood,
    ManageCookBills,
    ManageButcherBills,
    ManageFreezer,
    ManageFoodStockpile,
    TradeForFood,
    RecoverFromFoodEvent
}

public sealed record FoodLlmResponse
{
    [JsonConstructor]
    public FoodLlmResponse(
        string? StateSummary,
        IReadOnlyList<AdviceItem> Advice,
        IReadOnlyList<RimAI.Core.Ministers.AgentFlag> Flags,
        string? Notes = null)
    {
        this.StateSummary = StateSummary;
        this.Advice = Advice;
        this.Flags = Flags;
        this.Notes = Notes;
    }

    public FoodLlmResponse(
        IReadOnlyList<AdviceItem> Advice,
        IReadOnlyList<RimAI.Core.Ministers.AgentFlag> Flags,
        string? Notes = null)
        : this(null, Advice, Flags, Notes)
    {
    }

    [JsonPropertyName("state_summary")]
    public string? StateSummary { get; init; }

    [JsonPropertyName("advice")]
    public IReadOnlyList<AdviceItem> Advice { get; init; }

    [JsonPropertyName("flags")]
    public IReadOnlyList<RimAI.Core.Ministers.AgentFlag> Flags { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
