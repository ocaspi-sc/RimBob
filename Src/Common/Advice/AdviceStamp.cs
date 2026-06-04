using System.Text.Json.Serialization;
using RimBob.Core.Briefings;

namespace RimBob.Core.Advice;

public sealed record AdviceStamp(
    [property: JsonPropertyName("issued_at")]
    DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expires_at")]
    DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("issued_game_date")]
    GameDate? IssuedGameDate = null,
    [property: JsonPropertyName("issued_game_tick")]
    long? IssuedGameTick = null,
    [property: JsonPropertyName("expires_game_tick")]
    long? ExpiresGameTick = null);
