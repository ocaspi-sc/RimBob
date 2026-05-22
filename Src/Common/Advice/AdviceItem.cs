using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

/// <summary>
/// Feeder-minister advice item. The Mayor's Agenda is the MVP advice surface;
/// feeder ministers start publishing AdviceItems in M3+.
/// </summary>
public sealed record AdviceItem(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("minister")]
    string Minister,
    [property: JsonPropertyName("advice_type")]
    string AdviceType,
    [property: JsonPropertyName("priority")]
    AdvicePriority Priority,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("body")]
    string Body,
    [property: JsonPropertyName("rationale")]
    string Rationale,
    [property: JsonPropertyName("actions")]
    IReadOnlyList<AdviceAction> Actions,
    [property: JsonPropertyName("guide_citations")]
    IReadOnlyList<string> GuideCitationIds,
    [property: JsonPropertyName("issued_at")]
    DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expires_at")]
    DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("issued_in_game_tick")]
    string? IssuedInGameTick = null,
    [property: JsonPropertyName("issued_game_tick")]
    long? IssuedGameTick = null,
    [property: JsonPropertyName("expires_game_tick")]
    long? ExpiresGameTick = null,
    [property: JsonPropertyName("briefing_ref")]
    BriefingRef? BriefingRef = null,
    [property: JsonPropertyName("supersedes")]
    string? Supersedes = null,
    [property: JsonPropertyName("autonomy_at_issue")]
    AutonomyMode AutonomyAtIssue = AutonomyMode.Suggest,
    [property: JsonPropertyName("options")]
    IReadOnlyList<AdviceOption>? Options = null);

public sealed record BriefingRef(
    [property: JsonPropertyName("minister")]
    string Minister,
    [property: JsonPropertyName("version")]
    long Version,
    [property: JsonPropertyName("hash")]
    string Hash);
