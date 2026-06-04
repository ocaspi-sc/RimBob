using System.Text.Json;
using System.Text.Json.Nodes;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.LLM;

internal sealed record NormalizedFlagRequests(
    IReadOnlyList<BuildingRequest> BuildingRequests,
    IReadOnlyList<LaborRequest> LaborRequests,
    IReadOnlyList<ItemRequest> ItemRequests,
    IReadOnlyList<AttentionRequest> Attention)
{
    public static NormalizedFlagRequests Empty => new([], [], [], []);

    public IReadOnlyList<BuildingRequest>? BuildingRequestsOrNull => NullIfEmpty(BuildingRequests);
    public IReadOnlyList<LaborRequest>? LaborRequestsOrNull => NullIfEmpty(LaborRequests);
    public IReadOnlyList<ItemRequest>? ItemRequestsOrNull => NullIfEmpty(ItemRequests);
    public IReadOnlyList<AttentionRequest>? AttentionOrNull => NullIfEmpty(Attention);

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) =>
        values.Count == 0 ? null : values;
}

internal static class ResourceRequestNormalizer
{
    public static NormalizedFlagRequests NormalizeFlagRequests(
        JsonNode? flagNode,
        Priority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        RequestAccumulator accumulator = new();
        JsonObject? flag = flagNode as JsonObject;
        if (flag is null) return NormalizedFlagRequests.Empty;

        AddBuildingRequests(accumulator, flag["building_requests"], priority, json);
        AddLaborRequests(accumulator, flag["labor_requests"], priority, json);
        AddItemRequests(accumulator, flag["item_requests"], priority, json);
        AddAttentionRequests(accumulator, flag["attention"], priority, json);
        AddLegacyRequests(accumulator, flag["requests"], priority, context, json);

        return accumulator.ToRequests();
    }

    public static NormalizedFlagRequests NormalizeLegacyRequests(
        JsonNode? requestsNode,
        Priority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        RequestAccumulator accumulator = new();
        AddLegacyRequests(accumulator, requestsNode, priority, context, json);
        return accumulator.ToRequests();
    }

    public static AgentFlag Normalize(AgentFlag flag) =>
        flag with
        {
            BuildingRequests = NullIfEmpty(flag.BuildingRequests?.Select(request => Normalize(request)).ToArray()),
            LaborRequests = NullIfEmpty(flag.LaborRequests?.Select(request => Normalize(request)).ToArray()),
            ItemRequests = NullIfEmpty(flag.ItemRequests?.Select(request => Normalize(request)).ToArray()),
            Attention = NullIfEmpty(flag.Attention?.Select(request => Normalize(request)).ToArray())
        };

    public static BuildingRequest Normalize(BuildingRequest request) =>
        request with
        {
            Request = CleanRequired(request.Request, "building request"),
            Reason = CleanRequired(request.Reason, "building dependency"),
            TargetDef = CleanOptional(request.TargetDef),
            Adjacency = NullIfEmpty(request.Adjacency?
                .Where(hint => !string.IsNullOrWhiteSpace(hint.Target))
                .Select(hint => hint with { Target = hint.Target.Trim() })
                .ToArray()),
            MaterialsOnHand = NullIfEmpty(request.MaterialsOnHand?
                .Where(hint => !string.IsNullOrWhiteSpace(hint.Material))
                .Select(hint => hint with { Material = hint.Material.Trim() })
                .ToArray()),
            RequestedFrom = CleanOptional(request.RequestedFrom) ?? "Willie"
        };

    public static LaborRequest Normalize(LaborRequest request)
    {
        WorkType workType = request.WorkType;
        string? skill = string.IsNullOrWhiteSpace(request.Skill)
            ? WorkTypeInference.DefaultSkill(workType)
            : request.Skill.Trim();

        return request with
        {
            Request = CleanRequired(request.Request, "labor request"),
            Reason = CleanRequired(request.Reason, "labor dependency"),
            Skill = skill,
            RequestedFrom = CleanOptional(request.RequestedFrom) ?? "Labor"
        };
    }

    public static ItemRequest Normalize(ItemRequest request) =>
        request with
        {
            Request = CleanRequired(request.Request, "item request"),
            Reason = CleanRequired(request.Reason, "item dependency"),
            ItemDef = CleanOptional(request.ItemDef),
            RequestedFrom = CleanOptional(request.RequestedFrom)
        };

    public static AttentionRequest Normalize(AttentionRequest request) =>
        request with
        {
            Request = CleanRequired(request.Request, "attention request"),
            Reason = CleanRequired(request.Reason, "cross-minister dependency"),
            RequestedFrom = CleanOptional(request.RequestedFrom)
        };

    private static void AddBuildingRequests(
        RequestAccumulator accumulator,
        JsonNode? node,
        Priority priority,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return;

        foreach (JsonNode? item in array)
        {
            BuildingRequest? request = NormalizeBuildingItem(item, priority, json);
            if (request is not null) accumulator.BuildingRequests.Add(request);
        }
    }

    private static void AddLaborRequests(
        RequestAccumulator accumulator,
        JsonNode? node,
        Priority priority,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return;

        foreach (JsonNode? item in array)
        {
            LaborRequest? request = NormalizeLaborItem(item, priority, json, out AttentionRequest? attention);
            if (request is not null) accumulator.LaborRequests.Add(request);
            if (attention is not null) accumulator.Attention.Add(attention);
        }
    }

    private static void AddItemRequests(
        RequestAccumulator accumulator,
        JsonNode? node,
        Priority priority,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return;

        foreach (JsonNode? item in array)
        {
            ItemRequest? request = NormalizeItemItem(item, priority, json);
            if (request is not null) accumulator.ItemRequests.Add(request);
        }
    }

    private static void AddAttentionRequests(
        RequestAccumulator accumulator,
        JsonNode? node,
        Priority priority,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return;

        foreach (JsonNode? item in array)
        {
            AttentionRequest? request = NormalizeAttentionItem(item, priority, json);
            if (request is not null) accumulator.Attention.Add(request);
        }
    }

    private static void AddLegacyRequests(
        RequestAccumulator accumulator,
        JsonNode? node,
        Priority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        JsonArray? array = node?.AsArray();
        if (array is null) return;

        foreach (JsonNode? item in array)
        {
            if (item is null) continue;
            if (item is not JsonObject legacy)
            {
                string? text = LlmResponseParser.ReadString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    accumulator.Attention.Add(new AttentionRequest(
                        text.Trim(),
                        $"{context.Minister} LLM requested this resource.",
                        Priority: PriorityIfHigh(priority)));
                }

                continue;
            }

            AddLegacyObject(accumulator, legacy, priority, context, json);
        }
    }

    private static void AddLegacyObject(
        RequestAccumulator accumulator,
        JsonObject item,
        Priority priority,
        LlmAdviceNormalizationContext context,
        JsonSerializerOptions json)
    {
        string rawType = LlmResponseParser.ReadString(item["resource_type"]) ??
                         LlmResponseParser.ReadString(item["kind"]) ??
                         LlmResponseParser.ReadString(item["type"]) ??
                         "attention";
        string? amount = LlmResponseParser.ReadNumberAsString(item["amount"] ?? item["quantity"]);
        string? unit = LlmResponseParser.ReadString(item["unit"]);
        string reason = ReadReason(item, $"{context.Minister} LLM requested this resource.");
        string request = ReadRequest(item, FormatResourceRequest(rawType, amount, unit));
        int? quantity = LlmResponseParser.TryReadIntegerQuantity(item["quantity"] ?? item["amount"]);
        Priority? requestPriority = ReadPriority(item, priority);
        string? requestedFrom = CleanOptional(LlmResponseParser.ReadString(item["requested_from"]));

        LegacyRequestKind kind = ParseLegacyKind(rawType, request, reason);
        switch (kind)
        {
            case LegacyRequestKind.Labor:
                WorkType? workType = WorkTypeInference.Parse(LlmResponseParser.ReadString(item["work_type"])) ??
                                     WorkTypeInference.Infer(rawType, request, reason);
                if (workType is null)
                {
                    accumulator.Attention.Add(new AttentionRequest(
                        request,
                        $"{reason} Normalized as attention because the labor request did not name a RimWorld work type.",
                        Priority: requestPriority,
                        RequestedFrom: requestedFrom));
                    return;
                }

                accumulator.LaborRequests.Add(new LaborRequest(
                    request,
                    reason,
                    workType.Value,
                    Skill: CleanOptional(LlmResponseParser.ReadString(item["skill"])) ??
                           WorkTypeInference.DefaultSkill(workType),
                    Quantity: quantity,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom ?? "Labor"));
                return;

            case LegacyRequestKind.Building:
                accumulator.BuildingRequests.Add(new BuildingRequest(
                    request,
                    reason,
                    InferBuildingClass(rawType, request, reason),
                    TargetDef: CleanOptional(LlmResponseParser.ReadString(item["target_def"])) ??
                               InferTargetDef(rawType, request, reason),
                    RoomClass: InferRoomClass(rawType, request, reason),
                    Quantity: quantity,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom ?? "Willie"));
                return;

            case LegacyRequestKind.StockpileSpace:
                accumulator.BuildingRequests.Add(new BuildingRequest(
                    request,
                    reason,
                    BuildingClass.Stockpile,
                    RoomClass: RoomClass.Storage,
                    CapacityNeed: quantity is null
                        ? null
                        : new CapacityNeed(CapacityMeasure.StorageStacks, quantity, unit ?? "count"),
                    Quantity: quantity,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom ?? "Willie"));
                return;

            case LegacyRequestKind.Item:
                accumulator.ItemRequests.Add(new ItemRequest(
                    request,
                    reason,
                    ItemDef: CleanOptional(LlmResponseParser.ReadString(item["item_def"])) ??
                             InferItemDef(rawType, request, reason),
                    Quantity: quantity,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom));
                return;

            case LegacyRequestKind.Bill:
                accumulator.Attention.Add(new AttentionRequest(
                    request,
                    reason,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom ?? context.Minister));
                return;

            case LegacyRequestKind.Tile:
            case LegacyRequestKind.TradeCapacity:
            case LegacyRequestKind.Attention:
            default:
                accumulator.Attention.Add(new AttentionRequest(
                    request,
                    reason,
                    Priority: requestPriority,
                    RequestedFrom: requestedFrom));
                return;
        }
    }

    private static BuildingRequest? NormalizeBuildingItem(
        JsonNode? item,
        Priority priority,
        JsonSerializerOptions json)
    {
        if (item is not JsonObject obj) return null;

        BuildingRequest? strict = LlmResponseParser.TryDeserialize<BuildingRequest>(item, json);
        if (strict is not null &&
            obj["target_class"] is not null &&
            !string.IsNullOrWhiteSpace(strict.Request) &&
            !string.IsNullOrWhiteSpace(strict.Reason))
        {
            return Normalize(strict with { Priority = strict.Priority ?? PriorityIfHigh(priority) });
        }

        string request = ReadRequest(obj, "building request");
        string reason = ReadReason(obj, "building dependency");
        string classificationText = $"{LlmResponseParser.ReadString(obj["target_class"])} {LlmResponseParser.ReadString(obj["target_def"])} {request} {reason}";

        return Normalize(new BuildingRequest(
            request,
            reason,
            ParseBuildingClass(LlmResponseParser.ReadString(obj["target_class"])) ??
                InferBuildingClass(classificationText, request, reason),
            TargetDef: CleanOptional(LlmResponseParser.ReadString(obj["target_def"])),
            RoomClass: ParseRoomClass(LlmResponseParser.ReadString(obj["room_class"])) ??
                       InferRoomClass(classificationText, request, reason),
            CapacityNeed: TryDeserialize<CapacityNeed>(obj["capacity_need"], json),
            Adjacency: TryDeserialize<IReadOnlyList<AdjacencyHint>>(obj["adjacency"], json),
            Power: TryDeserialize<PowerNeed>(obj["power"], json),
            Temperature: TryDeserialize<TempNeed>(obj["temperature"], json),
            MaterialsOnHand: TryDeserialize<IReadOnlyList<MaterialHint>>(obj["materials_on_hand"], json),
            Urgency: ParseUrgency(LlmResponseParser.ReadString(obj["urgency"])),
            Deadline: TryDeserialize<Deadline>(obj["deadline"], json),
            Quantity: LlmResponseParser.TryReadIntegerQuantity(obj["quantity"]),
            Priority: ReadPriority(obj, priority),
            RequestedFrom: CleanOptional(LlmResponseParser.ReadString(obj["requested_from"]))));
    }

    private static LaborRequest? NormalizeLaborItem(
        JsonNode? item,
        Priority priority,
        JsonSerializerOptions json,
        out AttentionRequest? attention)
    {
        attention = null;
        if (item is not JsonObject obj) return null;

        LaborRequest? strict = LlmResponseParser.TryDeserialize<LaborRequest>(item, json);
        if (strict is not null &&
            obj["work_type"] is not null &&
            !string.IsNullOrWhiteSpace(strict.Request) &&
            !string.IsNullOrWhiteSpace(strict.Reason))
        {
            return Normalize(strict with { Priority = strict.Priority ?? PriorityIfHigh(priority) });
        }

        string request = ReadRequest(obj, "labor request");
        string reason = ReadReason(obj, "labor dependency");
        WorkType? workType = WorkTypeInference.Parse(LlmResponseParser.ReadString(obj["work_type"])) ??
                             WorkTypeInference.Infer(request, reason);
        Priority? requestPriority = ReadPriority(obj, priority);

        if (workType is null)
        {
            attention = new AttentionRequest(
                request,
                $"{reason} Normalized as attention because the labor request did not name a RimWorld work type.",
                Priority: requestPriority,
                RequestedFrom: CleanOptional(LlmResponseParser.ReadString(obj["requested_from"])));
            return null;
        }

        return Normalize(new LaborRequest(
            request,
            reason,
            workType.Value,
            Skill: CleanOptional(LlmResponseParser.ReadString(obj["skill"])) ??
                   WorkTypeInference.DefaultSkill(workType),
            Quantity: LlmResponseParser.TryReadIntegerQuantity(obj["quantity"] ?? obj["amount"]),
            Priority: requestPriority,
            RequestedFrom: CleanOptional(LlmResponseParser.ReadString(obj["requested_from"]))));
    }

    private static ItemRequest? NormalizeItemItem(
        JsonNode? item,
        Priority priority,
        JsonSerializerOptions json)
    {
        if (item is not JsonObject obj) return null;

        ItemRequest? strict = LlmResponseParser.TryDeserialize<ItemRequest>(item, json);
        if (strict is not null &&
            !string.IsNullOrWhiteSpace(strict.Request) &&
            !string.IsNullOrWhiteSpace(strict.Reason))
        {
            return Normalize(strict with { Priority = strict.Priority ?? PriorityIfHigh(priority) });
        }

        string request = ReadRequest(obj, "item request");
        string reason = ReadReason(obj, "item dependency");
        return Normalize(new ItemRequest(
            request,
            reason,
            ItemDef: CleanOptional(LlmResponseParser.ReadString(obj["item_def"])) ??
                     InferItemDef(null, request, reason),
            Quantity: LlmResponseParser.TryReadIntegerQuantity(obj["quantity"] ?? obj["amount"]),
            Priority: ReadPriority(obj, priority),
            RequestedFrom: CleanOptional(LlmResponseParser.ReadString(obj["requested_from"]))));
    }

    private static AttentionRequest? NormalizeAttentionItem(
        JsonNode? item,
        Priority priority,
        JsonSerializerOptions json)
    {
        if (item is null) return null;
        if (item is not JsonObject obj)
        {
            string? text = LlmResponseParser.ReadString(item);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : new AttentionRequest(text.Trim(), "LLM emitted this attention request.", Priority: PriorityIfHigh(priority));
        }

        AttentionRequest? strict = LlmResponseParser.TryDeserialize<AttentionRequest>(item, json);
        if (strict is not null &&
            !string.IsNullOrWhiteSpace(strict.Request) &&
            !string.IsNullOrWhiteSpace(strict.Reason))
        {
            return Normalize(strict with { Priority = strict.Priority ?? PriorityIfHigh(priority) });
        }

        return Normalize(new AttentionRequest(
            ReadRequest(obj, "attention request"),
            ReadReason(obj, "cross-minister dependency"),
            Priority: ReadPriority(obj, priority),
            RequestedFrom: CleanOptional(LlmResponseParser.ReadString(obj["requested_from"]))));
    }

    private static T? TryDeserialize<T>(JsonNode? node, JsonSerializerOptions json)
    {
        if (node is null) return default;
        return LlmResponseParser.TryDeserialize<T>(node, json);
    }

    private static string ReadRequest(JsonObject item, string fallback) =>
        CleanRequired(
            LlmResponseParser.ReadString(item["request"]) ??
            LlmResponseParser.ReadString(item["what"]) ??
            LlmResponseParser.ReadString(item["label"]),
            fallback);

    private static string ReadReason(JsonObject item, string fallback) =>
        CleanRequired(
            LlmResponseParser.ReadString(item["reason"]) ??
            LlmResponseParser.ReadString(item["description"]) ??
            LlmResponseParser.ReadString(item["why"]),
            fallback);

    private static Priority? ReadPriority(JsonObject item, Priority defaultPriority)
    {
        string? raw = LlmResponseParser.ReadString(item["priority"]);
        if (!string.IsNullOrWhiteSpace(raw) &&
            Enum.TryParse(raw.Replace("_", string.Empty), ignoreCase: true, out Priority priority))
        {
            return priority;
        }

        return PriorityIfHigh(defaultPriority);
    }

    private static Priority? PriorityIfHigh(Priority priority) =>
        priority >= Priority.High ? priority : null;

    private static LegacyRequestKind ParseLegacyKind(string rawType, string request, string reason)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {request} {reason}");
        if (normalized.Contains("labor") || normalized.Contains("pawnhour")) return LegacyRequestKind.Labor;
        if (normalized.Contains("stockpile") || normalized.Contains("shelf") || normalized.Contains("storagezone")) return LegacyRequestKind.StockpileSpace;
        if (normalized.Contains("cooler") || normalized.Contains("freezer") || normalized.Contains("building") || normalized.Contains("construction")) return LegacyRequestKind.Building;
        if (normalized.Contains("bill") || normalized.Contains("cook") || normalized.Contains("butcher")) return LegacyRequestKind.Bill;
        if (normalized.Contains("trade") || normalized.Contains("silver") || normalized.Contains("buy")) return LegacyRequestKind.TradeCapacity;
        if (normalized.Contains("tile") || normalized.Contains("zone")) return LegacyRequestKind.Tile;
        if (normalized.Contains("component") || normalized.Contains("steel") || normalized.Contains("medicine") || normalized.Contains("item")) return LegacyRequestKind.Item;
        return LegacyRequestKind.Attention;
    }

    private static BuildingClass InferBuildingClass(string? rawType, string request, string reason)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {request} {reason}");
        if (normalized.Contains("freezer") || normalized.Contains("coldstorage")) return BuildingClass.Freezer;
        if (normalized.Contains("cooler")) return BuildingClass.Cooler;
        if (normalized.Contains("heater")) return BuildingClass.Heater;
        if (normalized.Contains("battery")) return BuildingClass.Battery;
        if (normalized.Contains("power")) return BuildingClass.PowerGeneration;
        if (normalized.Contains("conduit")) return BuildingClass.Conduit;
        if (normalized.Contains("vent")) return BuildingClass.Vent;
        if (normalized.Contains("shelf")) return BuildingClass.Shelf;
        if (normalized.Contains("stockpile") || normalized.Contains("storage")) return BuildingClass.Stockpile;
        if (normalized.Contains("dumping")) return BuildingClass.DumpingZone;
        if (normalized.Contains("beacon")) return BuildingClass.TradeBeacon;
        if (normalized.Contains("research")) return BuildingClass.ResearchBench;
        if (normalized.Contains("multianalyzer")) return BuildingClass.Multianalyzer;
        if (normalized.Contains("bed")) return BuildingClass.Bed;
        if (normalized.Contains("floor")) return BuildingClass.Floor;
        if (normalized.Contains("roof")) return BuildingClass.Roof;
        if (normalized.Contains("wall")) return BuildingClass.Wall;
        if (normalized.Contains("door")) return BuildingClass.Door;
        if (normalized.Contains("barricade")) return BuildingClass.Barricade;
        if (normalized.Contains("embrasure")) return BuildingClass.Embrasure;
        return BuildingClass.ProductionBench;
    }

    private static BuildingClass? ParseBuildingClass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (BuildingClass value in Enum.GetValues<BuildingClass>())
        {
            if (LlmResponseParser.NormalizeIdentifier(value.ToString()) == normalized)
                return value;
        }

        return null;
    }

    private static RoomClass? InferRoomClass(string? rawType, string request, string reason)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {request} {reason}");
        if (normalized.Contains("freezer") || normalized.Contains("coldstorage")) return RoomClass.Freezer;
        if (normalized.Contains("kitchen") || normalized.Contains("stove") || normalized.Contains("meal")) return RoomClass.Kitchen;
        if (normalized.Contains("butcher")) return RoomClass.Butcher;
        if (normalized.Contains("hospital")) return RoomClass.Hospital;
        if (normalized.Contains("research")) return RoomClass.Research;
        if (normalized.Contains("workshop")) return RoomClass.Workshop;
        if (normalized.Contains("bedroom")) return RoomClass.Bedroom;
        if (normalized.Contains("barracks")) return RoomClass.Barracks;
        if (normalized.Contains("prison")) return RoomClass.Prison;
        if (normalized.Contains("recreation")) return RoomClass.Recreation;
        if (normalized.Contains("dining")) return RoomClass.Dining;
        if (normalized.Contains("stockpile") || normalized.Contains("storage")) return RoomClass.Storage;
        return null;
    }

    private static RoomClass? ParseRoomClass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (RoomClass value in Enum.GetValues<RoomClass>())
        {
            if (LlmResponseParser.NormalizeIdentifier(value.ToString()) == normalized)
                return value;
        }

        return null;
    }

    private static Urgency? ParseUrgency(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (Urgency value in Enum.GetValues<Urgency>())
        {
            if (LlmResponseParser.NormalizeIdentifier(value.ToString()) == normalized)
                return value;
        }

        return null;
    }

    private static string? InferTargetDef(string? rawType, string request, string reason)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {request} {reason}");
        if (normalized.Contains("cooler")) return "Cooler";
        if (normalized.Contains("campfire")) return "Campfire";
        if (normalized.Contains("stove")) return "FueledStove";
        if (normalized.Contains("butchertable")) return "TableButcher";
        return null;
    }

    private static string? InferItemDef(string? rawType, string request, string reason)
    {
        string normalized = LlmResponseParser.NormalizeIdentifier($"{rawType} {request} {reason}");
        if (normalized.Contains("component")) return "ComponentIndustrial";
        if (normalized.Contains("steel")) return "Steel";
        if (normalized.Contains("medicine")) return "MedicineIndustrial";
        if (normalized.Contains("meal")) return "MealSimple";
        return null;
    }

    private static string FormatResourceRequest(string rawType, string? amount, string? unit)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(amount)) parts.Add(amount);
        if (!string.IsNullOrWhiteSpace(unit)) parts.Add(unit);
        parts.Add(LlmResponseParser.HumanizeIdentifier(rawType).ToLowerInvariant());
        return string.Join(" ", parts);
    }

    private static string CleanRequired(string? value, string fallback)
    {
        string? clean = CleanOptional(value);
        return clean ?? fallback;
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? values) =>
        values is null || values.Count == 0 ? null : values;

    private sealed class RequestAccumulator
    {
        public List<BuildingRequest> BuildingRequests { get; } = [];
        public List<LaborRequest> LaborRequests { get; } = [];
        public List<ItemRequest> ItemRequests { get; } = [];
        public List<AttentionRequest> Attention { get; } = [];

        public NormalizedFlagRequests ToRequests() =>
            new(BuildingRequests, LaborRequests, ItemRequests, Attention);
    }

    private enum LegacyRequestKind
    {
        Labor,
        Tile,
        Item,
        Building,
        Bill,
        StockpileSpace,
        Attention,
        TradeCapacity
    }
}
