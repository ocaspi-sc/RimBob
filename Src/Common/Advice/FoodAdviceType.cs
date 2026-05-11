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

public sealed record FoodLlmResponse(
    [property: JsonPropertyName("advice")]
    IReadOnlyList<AdviceItem> Advice,
    [property: JsonPropertyName("flags")]
    IReadOnlyList<RimAI.Core.Ministers.AgentFlag> Flags,
    [property: JsonPropertyName("notes")]
    string? Notes = null
);
