using System.Globalization;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;

namespace RimBob.Ministers.Willie;

public sealed class Rules : IMinisterRules<WillieBriefing>
{
    public const string BuildingRequestActiveTrace = "building_request_active";

    private const string MinisterName = "Willie";
    private const string Domain = "construction";
    private const float LowBatteryReserveRatio = 0.25f;
    private readonly TimeProvider timeProvider;

    public Rules(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RuleRun Evaluate(WillieBriefing briefing) =>
        Evaluate(briefing, []);

    public RuleRun Evaluate(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<MinisterRule<WillieBriefing>> ruleTable = RuleTable(now, inboundBuildingRequests);
        IReadOnlyList<MinisterRuleTraceDescriptor<WillieBriefing>> fallbackRuleDescriptors =
            FallbackRuleDescriptors(inboundBuildingRequests);
        RuleRun ruleRun = MinisterRuleTableEvaluator.EvaluateAllHits(
            ruleTable,
            briefing,
            additionalRuleDescriptors: fallbackRuleDescriptors);
        if (ruleRun.Decisions.Count > 0)
            return ruleRun;

        return new RuleRun(
            [],
            DiagnosticsForFallback(ruleTable, fallbackRuleDescriptors, briefing, inboundBuildingRequests, "maintain_build_program"));
    }

    private static IReadOnlyList<MinisterRule<WillieBriefing>> RuleTable(
        DateTimeOffset now,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests) =>
    [
        new("power_net_deficit", MatchesPowerNetDeficit, PowerNetDeficitReason, briefing => BuildPowerNetDeficit(briefing, now)),
        new("low_battery_reserve", MatchesLowBatteryReserve, LowBatteryReserveReason, briefing => BuildLowBatteryReserve(briefing, now)),
        new("backlog_material_gap", MatchesBacklogMaterialGap, BacklogMaterialGapReason, briefing => BuildBacklogMaterialGap(briefing, now)),
        new("frame_blocked_by_material", MatchesFrameBlockedByMaterial, FrameBlockedByMaterialReason, briefing => BuildFrameBlockedByMaterial(briefing, now)),
        new(BuildingRequestActiveTrace, briefing => MatchesBuildingRequestActive(briefing, inboundBuildingRequests), briefing => BuildingRequestActiveReason(briefing, inboundBuildingRequests), briefing => BuildBuildingRequestActive(briefing, inboundBuildingRequests, now)),
        new("kitchen_missing", MatchesKitchenMissing, KitchenMissingReason, briefing => BuildMissingRoom(briefing, now, "kitchen_missing", RoomClass.Kitchen, Priority.Medium, "Kitchen is missing", "No kitchen anchor is visible in the current room inventory.")),
        new("hospital_missing", MatchesHospitalMissing, HospitalMissingReason, briefing => BuildMissingRoom(briefing, now, "hospital_missing", RoomClass.Hospital, Priority.Low, "Hospital is missing", "No hospital anchor is visible for a colony large enough to need a treatment room.")),
        new("storage_room_missing", MatchesStorageRoomMissing, StorageRoomMissingReason, briefing => BuildMissingRoom(briefing, now, "storage_room_missing", RoomClass.Storage, Priority.Low, "Storage room is missing", "No storage room anchor or stockpile zone is visible.")),
        new("cooler_missing", MatchesCoolerMissing, CoolerMissingReason, briefing => BuildCoolerMissing(briefing, now))
    ];

    private static bool MatchesPowerNetDeficit(WillieBriefing briefing) =>
        briefing.DataCoverage.HasPower && briefing.PowerStability.NetW < 0;

    private static string PowerNetDeficitReason(WillieBriefing briefing)
    {
        if (!briefing.DataCoverage.HasPower)
            return "power coverage is unavailable";

        return briefing.PowerStability.NetW < 0
            ? $"net_w={FormatWatts(briefing.PowerStability.NetW)}"
            : $"net_w={FormatWatts(briefing.PowerStability.NetW)} is not negative";
    }

    private static IReadOnlyList<Decision> BuildPowerNetDeficit(WillieBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(
            briefing,
            now,
            "power_net_deficit",
            Priority.High,
            "Power is running negative",
            $"The colony is drawing {FormatWatts(Math.Abs(briefing.PowerStability.NetW))} more power than it produces.",
            "A persistent net deficit will shut down critical infrastructure before new construction can rely on powered assets.",
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Add generation, reduce load, or plan batteries before the deficit drains the network.", Owner: MinisterName)],
            WillieFlagRequests.Empty);

    private static bool MatchesLowBatteryReserve(WillieBriefing briefing) =>
        briefing.DataCoverage.HasPower && HasLowBatteryReserve(briefing.PowerStability);

    private static string LowBatteryReserveReason(WillieBriefing briefing)
    {
        if (!briefing.DataCoverage.HasPower)
            return "power coverage is unavailable";

        if (briefing.PowerStability.CapacityWd <= 0)
            return "battery capacity is unavailable";

        return HasLowBatteryReserve(briefing.PowerStability)
            ? $"reserve={ReserveRatio(briefing.PowerStability):P0}"
            : $"reserve={ReserveRatio(briefing.PowerStability):P0} is at or above the {LowBatteryReserveRatio:P0} threshold";
    }

    private static IReadOnlyList<Decision> BuildLowBatteryReserve(WillieBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(
            briefing,
            now,
            "low_battery_reserve",
            Priority.Medium,
            "Battery reserve is thin",
            $"Stored power is {ReserveRatio(briefing.PowerStability):P0} of capacity.",
            "Low reserve leaves coolers, benches, and defensive loads fragile during eclipse, flare recovery, or generator interruptions.",
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Plan additional battery backup or generation margin for critical powered rooms.", Owner: MinisterName)],
            WillieFlagRequests.Empty);

    private static bool MatchesBacklogMaterialGap(WillieBriefing briefing) =>
        briefing.MaterialBottleneck.MissingMaterials.Count > 0;

    private static string BacklogMaterialGapReason(WillieBriefing briefing) =>
        briefing.MaterialBottleneck.MissingMaterials.Count > 0
            ? FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)
            : "construction backlog reports no missing materials";

    private static IReadOnlyList<Decision> BuildBacklogMaterialGap(WillieBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(
            briefing,
            now,
            "backlog_material_gap",
            Priority.High,
            "Build queue is short on materials",
            $"Queued construction is missing {FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)}.",
            "The backlog already reports missing materials, so adding new blueprints will increase queue debt instead of improving the base.",
            [new AdviceAction(AdviceActionKind.RequestResource, $"Acquire or haul {FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)} before expanding the build queue.", Quantity: briefing.MaterialBottleneck.MissingMaterials.Sum(material => material.Count), Owner: MinisterName)],
            ItemRequests(briefing.MaterialBottleneck.MissingMaterials, Priority.High));

    private static bool MatchesFrameBlockedByMaterial(WillieBriefing briefing) =>
        briefing.StalledBuilds.BlockedCount > 0;

    private static string FrameBlockedByMaterialReason(WillieBriefing briefing) =>
        briefing.StalledBuilds.BlockedCount > 0
            ? $"blocked_count={briefing.StalledBuilds.BlockedCount}"
            : "no blocked queued build items are visible";

    private static IReadOnlyList<Decision> BuildFrameBlockedByMaterial(WillieBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(
            briefing,
            now,
            "frame_blocked_by_material",
            Priority.High,
            "Construction is blocked",
            $"{briefing.StalledBuilds.BlockedCount} queued build item{Plural(briefing.StalledBuilds.BlockedCount)} cannot progress.",
            "Blocked frames and blueprints tie up the plan until materials or access are resolved.",
            [new AdviceAction(AdviceActionKind.RequestResource, "Unblock the existing frames before placing more construction work.", Quantity: briefing.StalledBuilds.BlockedCount, Owner: MinisterName)],
            WillieFlagRequests.AttentionRequest(
                "unblock current construction queue",
                "blocked blueprint/frame count is non-zero",
                Priority.High,
                "Player"));

    private static bool MatchesBuildingRequestActive(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests) =>
        SelectPlacementRequest(briefing, inboundBuildingRequests) is { } request &&
        !ShouldDeferToMissingKitchen(briefing, request);

    private static string BuildingRequestActiveReason(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests)
    {
        BuildingRequest? request = SelectPlacementRequest(briefing, inboundBuildingRequests);
        if (request is null)
            return "no inbound Willie building requests are active";

        if (ShouldDeferToMissingKitchen(briefing, request))
            return $"{request.Request} depends on a missing kitchen, so kitchen_missing owns the prerequisite";

        return $"selected inbound Willie building request: {request.Request}";
    }

    private static IReadOnlyList<Decision> BuildBuildingRequestActive(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        DateTimeOffset now)
    {
        BuildingRequest request = SelectPlacementRequest(briefing, inboundBuildingRequests)
            ?? throw new InvalidOperationException("building_request_active matched without a selected placement request");
        bool freezing = IsFreezingBuildRequest(request);
        string target = FormatRequestTarget(request);
        string title = freezing
            ? "Freezer request needs Willie placement"
            : $"{TitleCase(target)} request needs Willie placement";
        string rationale = freezing
            ? "Chef owns the food-storage need; Willie owns the freezer shell, cooler, power, and eventual placement."
            : $"The requesting minister owns why this build matters; Willie owns the {target} footprint, materials, and placement.";

        return EmitAdvice(
            briefing,
            now,
            BuildingRequestActiveTrace,
            request.Priority ?? Priority.Medium,
            title,
            request.Request,
            rationale,
            [
                new AdviceAction(AdviceActionKind.PlaceBlueprint, $"Plan a {target} build for this request; solver options attach when a validated footprint is available.", Owner: MinisterName)
            ],
            WillieFlagRequests.Empty);
    }

    private static bool MatchesKitchenMissing(WillieBriefing briefing) =>
        HasFunctionalRoomEvidence(briefing) && MissingRoom(briefing, RoomClass.Kitchen);

    private static string KitchenMissingReason(WillieBriefing briefing)
    {
        if (!HasFunctionalRoomEvidence(briefing))
            return "functional-room evidence is unavailable";

        return MissingRoom(briefing, RoomClass.Kitchen)
            ? "no kitchen room class anchor"
            : "a kitchen room class anchor is visible";
    }

    private static bool MatchesHospitalMissing(WillieBriefing briefing) =>
        HasFunctionalRoomEvidence(briefing) &&
        briefing.ColonistCount >= 3 &&
        MissingRoom(briefing, RoomClass.Hospital);

    private static string HospitalMissingReason(WillieBriefing briefing)
    {
        if (!HasFunctionalRoomEvidence(briefing))
            return "functional-room evidence is unavailable";

        if (briefing.ColonistCount < 3)
            return $"{briefing.ColonistCount} colonists is below the hospital-room trigger";

        return MissingRoom(briefing, RoomClass.Hospital)
            ? "no hospital room class anchor"
            : "a hospital room class anchor is visible";
    }

    private static bool MatchesStorageRoomMissing(WillieBriefing briefing) =>
        HasFunctionalRoomEvidence(briefing) &&
        MissingRoom(briefing, RoomClass.Storage) &&
        briefing.StoragePlacement.StockpileZones == 0;

    private static string StorageRoomMissingReason(WillieBriefing briefing)
    {
        if (!HasFunctionalRoomEvidence(briefing))
            return "functional-room evidence is unavailable";

        if (!MissingRoom(briefing, RoomClass.Storage))
            return "a storage room class anchor is visible";

        return briefing.StoragePlacement.StockpileZones == 0
            ? "no storage anchor or stockpile zone"
            : $"{briefing.StoragePlacement.StockpileZones} stockpile zone{Plural(briefing.StoragePlacement.StockpileZones)} visible";
    }

    private static IReadOnlyList<Decision> BuildMissingRoom(
        WillieBriefing briefing,
        DateTimeOffset now,
        string trace,
        RoomClass roomClass,
        Priority priority,
        string title,
        string body) =>
        EmitAdvice(
            briefing,
            now,
            trace,
            priority,
            title,
            body,
            $"{FormatRoom(roomClass)} work is a built-infrastructure gap; the placement solver owns exact footprint options when map evidence supports them.",
            [
                new AdviceAction(AdviceActionKind.PlaceBlueprint, $"Plan a compact {FormatRoom(roomClass)} footprint; solver options attach when a validated footprint is available.", Owner: MinisterName)
            ],
            WillieFlagRequests.Empty);

    private static bool MatchesCoolerMissing(WillieBriefing briefing) =>
        briefing.DataCoverage.HasBuildings &&
        briefing.ThermalControl.CoolerCount == 0 &&
        briefing.ThermalControl.FreezerAnchorCount > 0;

    private static string CoolerMissingReason(WillieBriefing briefing)
    {
        if (!briefing.DataCoverage.HasBuildings)
            return "building coverage is unavailable";

        if (briefing.ThermalControl.CoolerCount > 0)
            return $"{briefing.ThermalControl.CoolerCount} cooler{Plural(briefing.ThermalControl.CoolerCount)} visible";

        return briefing.ThermalControl.FreezerAnchorCount > 0
            ? "freezer anchor exists without visible cooler"
            : "no freezer anchor currently needs a cooler";
    }

    private static IReadOnlyList<Decision> BuildCoolerMissing(WillieBriefing briefing, DateTimeOffset now) =>
        EmitAdvice(
            briefing,
            now,
            "cooler_missing",
            Priority.Medium,
            "Freezer has no visible cooler",
            "A freezer room anchor exists, but no cooler building is visible.",
            "Freezer rooms only preserve food if the thermal asset exists and is powered.",
            [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Add or repair a cooler for the freezer room.", Owner: MinisterName)],
            WillieFlagRequests.Empty);

    private static IReadOnlyList<Decision> EmitAdvice(
        WillieBriefing briefing,
        DateTimeOffset now,
        string trace,
        Priority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        WillieFlagRequests requests)
    {
        List<Decision> decisions =
        [
            new Advise(
                trace,
                priority,
                title,
                body,
                rationale,
                actions)
        ];
        decisions.AddRange(requests.ToDecisions(trace, priority));
        return decisions;
    }

    private static WillieFlagRequests ItemRequests(
        IReadOnlyList<MaterialCount> materials,
        Priority priority)
    {
        WillieFlagRequests requests = WillieFlagRequests.Empty;
        foreach (MaterialCount material in materials.Take(4))
        {
            requests = requests.Add(new ItemRequest(
                Request: $"{material.Count} {material.DefName}",
                Reason: "queued construction reports this material missing",
                ItemDef: material.DefName,
                Quantity: material.Count,
                Priority: priority,
                RequestedFrom: "Industry"));
        }

        return requests;
    }

    private static RuleTraceDetails DiagnosticsForFallback(
        IReadOnlyList<MinisterRule<WillieBriefing>> rules,
        IReadOnlyList<MinisterRuleTraceDescriptor<WillieBriefing>> fallbackRuleDescriptors,
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        string selectedRule)
    {
        return MinisterRuleTableEvaluator.BuildTrace(
            rules,
            briefing,
            decisions: [],
            matchedRules: new HashSet<RuleId> { selectedRule },
            additionalRuleDescriptors: fallbackRuleDescriptors,
            selectedRuleOverride: selectedRule);
    }

    private static IReadOnlyList<MinisterRuleTraceDescriptor<WillieBriefing>> FallbackRuleDescriptors(
        IReadOnlyList<BuildingRequest> inboundBuildingRequests) =>
    [
        new("maintain_build_program", briefing => MaintainBuildProgramTraceReason(briefing, inboundBuildingRequests), (_, selectedRule) => SelectedWhenSelected("maintain_build_program", selectedRule))
    ];

    private static string MaintainBuildProgramTraceReason(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests) =>
        AnyTableRuleApplies(briefing, inboundBuildingRequests)
            ? "one or more Willie rules matched, so maintenance fallback is not selected"
            : "no deterministic Willie rule matched; build program remains stable";

    private static bool AnyTableRuleApplies(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests) =>
        RuleTable(DateTimeOffset.UnixEpoch, inboundBuildingRequests).Any(rule => rule.Matches(briefing));

    private static RuleOutcome SelectedWhenSelected(RuleId rule, RuleId? selectedRule) =>
        selectedRule == rule ? RuleOutcome.Selected : RuleOutcome.NotMatched;

    private static bool HasLowBatteryReserve(WilliePowerStabilitySummary power) =>
        power.CapacityWd > 0 && ReserveRatio(power) < LowBatteryReserveRatio;

    private static float ReserveRatio(WilliePowerStabilitySummary power) =>
        power.CapacityWd <= 0 ? 0f : power.StoredWd / power.CapacityWd;

    private static bool HasFunctionalRoomEvidence(WillieBriefing briefing) =>
        briefing.DataCoverage.HasAnchorInventory ||
        briefing.FunctionalRooms.RoomCountsByClass.Count > 0;

    private static bool MissingRoom(WillieBriefing briefing, RoomClass roomClass) =>
        !briefing.FunctionalRooms.RoomCountsByClass.TryGetValue(roomClass.ToString(), out int count) ||
        count <= 0;

    public static bool TryGetPlacementRequest(
        RuleId trace,
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        out BuildingRequest request)
    {
        if (trace == BuildingRequestActiveTrace)
        {
            request = SelectPlacementRequest(briefing, inboundBuildingRequests)!;
            return request is not null;
        }

        if (TryMissingRoomClass(trace.Value, out RoomClass roomClass))
        {
            request = MissingRoomRequest(briefing, roomClass);
            return true;
        }

        request = null!;
        return false;
    }

    public static BuildingRequest? SelectPlacementRequest(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> requests) =>
        requests
            .OrderByDescending(request => MissingKitchenDependencyRank(briefing, request))
            .ThenByDescending(request => (int)(request.Priority ?? Priority.Medium))
            .ThenBy(request => FormatRequestTarget(request), StringComparer.OrdinalIgnoreCase)
            .ThenBy(request => request.Request, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static int MissingKitchenDependencyRank(WillieBriefing briefing, BuildingRequest request)
    {
        if (!HasFunctionalRoomEvidence(briefing) || !MissingRoom(briefing, RoomClass.Kitchen))
            return 1;

        if (IsKitchenBuildRequest(request))
            return 2;

        return DependsOnKitchen(request) ? 0 : 1;
    }

    private static bool ShouldDeferToMissingKitchen(WillieBriefing briefing, BuildingRequest request) =>
        HasFunctionalRoomEvidence(briefing) &&
        MissingRoom(briefing, RoomClass.Kitchen) &&
        DependsOnKitchen(request) &&
        !IsKitchenBuildRequest(request);

    private static bool IsKitchenBuildRequest(BuildingRequest request) =>
        request.RoomClass == RoomClass.Kitchen;

    private static bool DependsOnKitchen(BuildingRequest request) =>
        (request.Adjacency ?? []).Any(hint => TargetsRoom(hint, RoomClass.Kitchen));

    private static bool TargetsRoom(AdjacencyHint hint, RoomClass roomClass)
    {
        string target = hint.Target.Trim();
        return string.Equals(target, roomClass.ToString(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(target, ToSnakeCase(roomClass.ToString()), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFreezingBuildRequest(BuildingRequest request) =>
        request.Temperature?.TargetBand == TemperatureBand.Freezing ||
        request.RoomClass == RoomClass.Freezer ||
        request.TargetClass == BuildingClass.Freezer;

    public static bool TryMissingRoomClass(string trace, out RoomClass roomClass)
    {
        roomClass = trace switch
        {
            "kitchen_missing" => RoomClass.Kitchen,
            "hospital_missing" => RoomClass.Hospital,
            "storage_room_missing" => RoomClass.Storage,
            _ => default
        };
        return trace is "kitchen_missing" or "hospital_missing" or "storage_room_missing";
    }

    public static BuildingRequest MissingRoomRequest(WillieBriefing briefing, RoomClass roomClass)
    {
        RoomClass? anchorClass = PreferredAnchorClass(briefing, roomClass);
        IReadOnlyList<AdjacencyHint>? adjacency = anchorClass is null
            ? null
            : [new AdjacencyHint(AdjacencyRelation.Near, ToSnakeCase(anchorClass.Value.ToString()))];

        return new BuildingRequest(
            Request: $"starter {FormatRoom(roomClass)} footprint",
            Reason: $"Willie rules detected no {FormatRoom(roomClass)} anchor in the current room inventory.",
            TargetClass: TargetClassForMissingRoom(roomClass),
            TargetDef: TargetDefForMissingRoom(roomClass),
            RoomClass: roomClass,
            CapacityNeed: CapacityNeedForMissingRoom(roomClass),
            Adjacency: adjacency,
            Priority: MissingRoomPriority(roomClass),
            RequestedFrom: MinisterName);
    }

    private static RoomClass? PreferredAnchorClass(WillieBriefing briefing, RoomClass missingRoomClass)
    {
        IReadOnlyList<RoomClass> preferredClasses = PreferredAnchorClasses(missingRoomClass);
        foreach (RoomClass preferred in preferredClasses)
        {
            if (HasRoom(briefing, preferred))
                return preferred;
        }

        WillieRoomAnchor? anchor = briefing.AnchorInventory.Anchors
            .FirstOrDefault(anchor => anchor.Class != missingRoomClass);
        return anchor?.Class ?? preferredClasses.FirstOrDefault();
    }

    private static IReadOnlyList<RoomClass> PreferredAnchorClasses(RoomClass missingRoomClass) =>
        missingRoomClass switch
        {
            RoomClass.Kitchen => [RoomClass.Storage, RoomClass.Dining, RoomClass.Recreation],
            RoomClass.Hospital => [RoomClass.Bedroom, RoomClass.Barracks, RoomClass.Kitchen, RoomClass.Storage],
            RoomClass.Storage => [RoomClass.Kitchen, RoomClass.Workshop, RoomClass.Dining],
            _ => [RoomClass.Kitchen, RoomClass.Storage]
        };

    private static bool HasRoom(WillieBriefing briefing, RoomClass roomClass) =>
        briefing.FunctionalRooms.RoomCountsByClass.TryGetValue(roomClass.ToString(), out int count) &&
        count > 0;

    private static BuildingClass TargetClassForMissingRoom(RoomClass roomClass) =>
        roomClass switch
        {
            RoomClass.Kitchen => BuildingClass.ProductionBench,
            RoomClass.Hospital => BuildingClass.Bed,
            RoomClass.Storage => BuildingClass.Shelf,
            _ => BuildingClass.Wall
        };

    private static string? TargetDefForMissingRoom(RoomClass roomClass) =>
        roomClass switch
        {
            RoomClass.Kitchen => "FueledStove",
            RoomClass.Hospital => "Bed",
            RoomClass.Storage => "Shelf",
            _ => null
        };

    private static CapacityNeed? CapacityNeedForMissingRoom(RoomClass roomClass) =>
        roomClass switch
        {
            RoomClass.Kitchen => new CapacityNeed(CapacityMeasure.WorkSlots, 1),
            RoomClass.Hospital => new CapacityNeed(CapacityMeasure.Beds, 2),
            RoomClass.Storage => new CapacityNeed(CapacityMeasure.StorageStacks, 12),
            _ => null
        };

    private static Priority MissingRoomPriority(RoomClass roomClass) =>
        roomClass == RoomClass.Kitchen ? Priority.Medium : Priority.Low;

    private static string FormatMaterials(IReadOnlyList<MaterialCount> materials) =>
        string.Join(", ", materials.Take(4).Select(material => $"{material.Count} {material.DefName}"));

    private static string FormatWatts(float watts) =>
        $"{watts.ToString("0", CultureInfo.InvariantCulture)} W";

    private static string FormatRoom(RoomClass roomClass) =>
        ToSnakeCase(roomClass.ToString()).Replace('_', ' ');

    private static string FormatRequestTarget(BuildingRequest request) =>
        request.RoomClass is not null
            ? FormatRoom(request.RoomClass.Value)
            : ToSnakeCase(request.TargetClass.ToString()).Replace('_', ' ');

    private static string TitleCase(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static string ToSnakeCase(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
