using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

/// <summary>
/// A build dependency another minister needs Construction to notice.
/// Requests are advisory in MVP; they do not reserve tiles or write to RIMAPI.
/// </summary>
public sealed record BuildingRequest(
    [property: JsonPropertyName("request")]
    string Request,
    [property: JsonPropertyName("reason")]
    string Reason,
    [property: JsonPropertyName("target_class")]
    BuildingClass TargetClass,
    [property: JsonPropertyName("target_def")]
    string? TargetDef = null,
    [property: JsonPropertyName("room_class")]
    RoomClass? RoomClass = null,
    [property: JsonPropertyName("capacity_need")]
    CapacityNeed? CapacityNeed = null,
    [property: JsonPropertyName("adjacency")]
    IReadOnlyList<AdjacencyHint>? Adjacency = null,
    [property: JsonPropertyName("power")]
    PowerNeed? Power = null,
    [property: JsonPropertyName("temperature")]
    TempNeed? Temperature = null,
    [property: JsonPropertyName("materials_on_hand")]
    IReadOnlyList<MaterialHint>? MaterialsOnHand = null,
    [property: JsonPropertyName("urgency")]
    Urgency? Urgency = null,
    [property: JsonPropertyName("deadline")]
    Deadline? Deadline = null,
    [property: JsonPropertyName("quantity")]
    int? Quantity = null,
    [property: JsonPropertyName("priority")]
    AdvicePriority? Priority = null,
    [property: JsonPropertyName("requested_from")]
    string? RequestedFrom = null);

/// <summary>
/// Work-type-qualified labor another minister needs Labor/player attention for.
/// </summary>
public sealed record LaborRequest(
    [property: JsonPropertyName("request")]
    string Request,
    [property: JsonPropertyName("reason")]
    string Reason,
    [property: JsonPropertyName("work_type")]
    WorkType WorkType,
    [property: JsonPropertyName("skill")]
    string? Skill = null,
    [property: JsonPropertyName("quantity")]
    int? Quantity = null,
    [property: JsonPropertyName("priority")]
    AdvicePriority? Priority = null,
    [property: JsonPropertyName("requested_from")]
    string? RequestedFrom = null);

/// <summary>
/// Material, component, medicine, or other item dependency.
/// </summary>
public sealed record ItemRequest(
    [property: JsonPropertyName("request")]
    string Request,
    [property: JsonPropertyName("reason")]
    string Reason,
    [property: JsonPropertyName("item_def")]
    string? ItemDef = null,
    [property: JsonPropertyName("quantity")]
    int? Quantity = null,
    [property: JsonPropertyName("priority")]
    AdvicePriority? Priority = null,
    [property: JsonPropertyName("requested_from")]
    string? RequestedFrom = null);

/// <summary>
/// Catch-all for real dependencies that do not have a typed request array yet.
/// </summary>
public sealed record AttentionRequest(
    [property: JsonPropertyName("request")]
    string Request,
    [property: JsonPropertyName("reason")]
    string Reason,
    [property: JsonPropertyName("priority")]
    AdvicePriority? Priority = null,
    [property: JsonPropertyName("requested_from")]
    string? RequestedFrom = null);

public sealed record CapacityNeed(
    [property: JsonPropertyName("measure")]
    CapacityMeasure Measure,
    [property: JsonPropertyName("amount")]
    double? Amount = null,
    [property: JsonPropertyName("unit")]
    string? Unit = null);

public sealed record AdjacencyHint(
    [property: JsonPropertyName("relation")]
    AdjacencyRelation Relation,
    [property: JsonPropertyName("target")]
    string Target);

public sealed record PowerNeed(
    [property: JsonPropertyName("needs_power")]
    bool NeedsPower,
    [property: JsonPropertyName("approx_watts")]
    int? ApproxWatts = null);

public sealed record TempNeed(
    [property: JsonPropertyName("target_band")]
    TemperatureBand TargetBand,
    [property: JsonPropertyName("must_hold")]
    bool MustHold);

public sealed record MaterialHint(
    [property: JsonPropertyName("material")]
    string Material,
    [property: JsonPropertyName("approx_qty")]
    int? ApproxQty = null);

public sealed record Deadline(
    [property: JsonPropertyName("kind")]
    DeadlineKind Kind,
    [property: JsonPropertyName("value")]
    object? Value = null);

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<BuildingClass>))]
public enum BuildingClass
{
    Freezer,
    Wall,
    Door,
    Barricade,
    Embrasure,
    PowerGeneration,
    Battery,
    Conduit,
    Cooler,
    Heater,
    Vent,
    Bed,
    ProductionBench,
    ResearchBench,
    Multianalyzer,
    Stockpile,
    Shelf,
    DumpingZone,
    TradeBeacon,
    Floor,
    Roof,
    TurretPlatform
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<RoomClass>))]
public enum RoomClass
{
    Freezer,
    Hospital,
    Kitchen,
    Butcher,
    Workshop,
    Research,
    Bedroom,
    Barracks,
    Prison,
    Recreation,
    Dining,
    Storage
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<CapacityMeasure>))]
public enum CapacityMeasure
{
    Beds,
    FoodUnits,
    WorkSlots,
    StorageStacks,
    Occupants
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AdjacencyRelation>))]
public enum AdjacencyRelation
{
    Near,
    Inside,
    ConnectedTo,
    AwayFrom
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<TemperatureBand>))]
public enum TemperatureBand
{
    Freezing,
    Cold,
    Room,
    SterileWarm
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<Urgency>))]
public enum Urgency
{
    WhenConvenient,
    Soon,
    BeforeDeadline,
    BlockingNow
}

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<DeadlineKind>))]
public enum DeadlineKind
{
    ByDay,
    BySeason,
    BeforeEvent
}
