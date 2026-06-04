using System.Text.Json.Serialization;
using RimBob.Core.Briefings;

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
    [property: JsonPropertyName("priority")]
    Priority Priority,
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
    [property: JsonPropertyName("stamp")]
    AdviceStamp Stamp,
    [property: JsonPropertyName("briefing_ref")]
    BriefingRef? BriefingRef = null,
    [property: JsonPropertyName("autonomy_at_issue")]
    AutonomyMode AutonomyAtIssue = AutonomyMode.Suggest,
    [property: JsonPropertyName("options")]
    IReadOnlyList<AdviceOption>? Options = null)
{
    [JsonIgnore]
    public DateTimeOffset IssuedAt => Stamp.IssuedAt;

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => Stamp.ExpiresAt;

    [JsonIgnore]
    public GameDate? IssuedGameDate => Stamp.IssuedGameDate;

    [JsonIgnore]
    public long? IssuedGameTick => Stamp.IssuedGameTick;

    [JsonIgnore]
    public long? ExpiresGameTick => Stamp.ExpiresGameTick;
}

public sealed record BriefingRef(
    [property: JsonPropertyName("minister")]
    string Minister,
    [property: JsonPropertyName("version")]
    long Version,
    [property: JsonPropertyName("hash")]
    string Hash);
