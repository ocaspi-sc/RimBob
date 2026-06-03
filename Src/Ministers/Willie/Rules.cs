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

    public RulesResult Evaluate(WillieBriefing briefing, ColonyContext context) =>
        Evaluate(briefing, context, []);

    public RulesResult Evaluate(
        WillieBriefing briefing,
        ColonyContext context,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests)
    {
        if (briefing.DataCoverage.HasPower && briefing.PowerStability.NetW < 0)
            return DecisionFor(
                briefing,
                inboundBuildingRequests,
                "power_net_deficit",
                AdvicePriority.High,
                "Power is running negative",
                $"The colony is drawing {FormatWatts(Math.Abs(briefing.PowerStability.NetW))} more power than it produces.",
                "A persistent net deficit will shut down critical infrastructure before new construction can rely on powered assets.",
                [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Add generation, reduce load, or plan batteries before the deficit drains the network.", Owner: MinisterName)],
                WillieFlagRequests.Empty);

        if (briefing.DataCoverage.HasPower && HasLowBatteryReserve(briefing.PowerStability))
            return DecisionFor(
                briefing,
                inboundBuildingRequests,
                "low_battery_reserve",
                AdvicePriority.Medium,
                "Battery reserve is thin",
                $"Stored power is {ReserveRatio(briefing.PowerStability):P0} of capacity.",
                "Low reserve leaves coolers, benches, and defensive loads fragile during eclipse, flare recovery, or generator interruptions.",
                [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Plan additional battery backup or generation margin for critical powered rooms.", Owner: MinisterName)],
                WillieFlagRequests.Empty);

        if (briefing.MaterialBottleneck.MissingMaterials.Count > 0)
            return DecisionFor(
                briefing,
                inboundBuildingRequests,
                "backlog_material_gap",
                AdvicePriority.High,
                "Build queue is short on materials",
                $"Queued construction is missing {FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)}.",
                "The backlog already reports missing materials, so adding new blueprints will increase queue debt instead of improving the base.",
                [new AdviceAction(AdviceActionKind.RequestResource, $"Acquire or haul {FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)} before expanding the build queue.", Quantity: briefing.MaterialBottleneck.MissingMaterials.Sum(material => material.Count), Owner: MinisterName)],
                ItemRequests(briefing.MaterialBottleneck.MissingMaterials, AdvicePriority.High));

        if (briefing.StalledBuilds.BlockedCount > 0)
            return DecisionFor(
                briefing,
                inboundBuildingRequests,
                "frame_blocked_by_material",
                AdvicePriority.High,
                "Construction is blocked",
                $"{briefing.StalledBuilds.BlockedCount} queued build item{Plural(briefing.StalledBuilds.BlockedCount)} cannot progress.",
                "Blocked frames and blueprints tie up the plan until materials or access are resolved.",
                [new AdviceAction(AdviceActionKind.RequestResource, "Unblock the existing frames before placing more construction work.", Quantity: briefing.StalledBuilds.BlockedCount, Owner: MinisterName)],
                WillieFlagRequests.AttentionRequest(
                    "unblock current construction queue",
                    "blocked blueprint/frame count is non-zero",
                    AdvicePriority.High,
                    "Player"));

        BuildingRequest? buildingRequest = SelectPlacementRequest(briefing, inboundBuildingRequests);
        if (buildingRequest is not null && !ShouldDeferToMissingKitchen(briefing, buildingRequest))
            return BuildingRequestDecision(briefing, inboundBuildingRequests, buildingRequest);

        if (HasFunctionalRoomEvidence(briefing) && MissingRoom(briefing, RoomClass.Kitchen))
            return MissingRoomDecision(
                briefing,
                inboundBuildingRequests,
                "kitchen_missing",
                RoomClass.Kitchen,
                AdvicePriority.Medium,
                "Kitchen is missing",
                "No kitchen anchor is visible in the current room inventory.");

        if (HasFunctionalRoomEvidence(briefing) && briefing.ColonistCount >= 3 && MissingRoom(briefing, RoomClass.Hospital))
            return MissingRoomDecision(
                briefing,
                inboundBuildingRequests,
                "hospital_missing",
                RoomClass.Hospital,
                AdvicePriority.Low,
                "Hospital is missing",
                "No hospital anchor is visible for a colony large enough to need a treatment room.");

        if (HasFunctionalRoomEvidence(briefing) &&
            MissingRoom(briefing, RoomClass.Storage) &&
            briefing.StoragePlacement.StockpileZones == 0)
        {
            return MissingRoomDecision(
                briefing,
                inboundBuildingRequests,
                "storage_room_missing",
                RoomClass.Storage,
                AdvicePriority.Low,
                "Storage room is missing",
                "No storage room anchor or stockpile zone is visible.");
        }

        if (briefing.DataCoverage.HasBuildings &&
            briefing.ThermalControl.CoolerCount == 0 &&
            briefing.ThermalControl.FreezerAnchorCount > 0)
            return DecisionFor(
                briefing,
                inboundBuildingRequests,
                "cooler_missing",
                AdvicePriority.Medium,
                "Freezer has no visible cooler",
                "A freezer room anchor exists, but no cooler building is visible.",
                "Freezer rooms only preserve food if the thermal asset exists and is powered.",
                [new AdviceAction(AdviceActionKind.PlaceBlueprint, "Add or repair a cooler for the freezer room.", Owner: MinisterName)],
                WillieFlagRequests.Empty);

        return new Decision(
            [],
            [],
            "maintain_build_program",
            DiagnosticsFor(briefing, inboundBuildingRequests, "maintain_build_program"));
    }

    private Decision MissingRoomDecision(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        string trace,
        RoomClass roomClass,
        AdvicePriority priority,
        string title,
        string body) =>
        DecisionFor(
            briefing,
            inboundBuildingRequests,
            trace,
            priority,
            title,
            body,
            $"{FormatRoom(roomClass)} work is a built-infrastructure gap; the placement solver owns exact footprint options when map evidence supports them.",
            [
                new AdviceAction(AdviceActionKind.PlaceBlueprint, $"Plan a compact {FormatRoom(roomClass)} footprint; solver options attach when a validated footprint is available.", Owner: MinisterName)
            ],
            WillieFlagRequests.Empty);

    private Decision BuildingRequestDecision(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        BuildingRequest request)
    {
        bool freezing = IsFreezingBuildRequest(request);
        string target = FormatRequestTarget(request);
        string title = freezing
            ? "Freezer request needs Willie placement"
            : $"{TitleCase(target)} request needs Willie placement";
        string rationale = freezing
            ? "Chef owns the food-storage need; Willie owns the freezer shell, cooler, power, and eventual placement."
            : $"The requesting minister owns why this build matters; Willie owns the {target} footprint, materials, and placement.";

        return DecisionFor(
            briefing,
            inboundBuildingRequests,
            BuildingRequestActiveTrace,
            request.Priority ?? AdvicePriority.Medium,
            title,
            request.Request,
            rationale,
            [
                new AdviceAction(AdviceActionKind.PlaceBlueprint, $"Plan a {target} build for this request; solver options attach when a validated footprint is available.", Owner: MinisterName)
            ],
            WillieFlagRequests.Empty);
    }

    private Decision DecisionFor(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        string trace,
        AdvicePriority priority,
        string title,
        string body,
        string rationale,
        IReadOnlyList<AdviceAction> actions,
        WillieFlagRequests requests)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        AdviceItem advice = new(
            Id: $"{MinisterName.ToLowerInvariant()}_{trace}",
            Minister: MinisterName,
            Priority: priority,
            Title: title,
            Body: body,
            Rationale: rationale,
            Actions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(priority >= AdvicePriority.High ? 4 : 24),
            IssuedGameDate: briefing.Date,
            IssuedGameTick: briefing.GameTick,
            ExpiresGameTick: AdviceFreshness.ExpiresGameTick(briefing.GameTick, priority),
            BriefingRef: new BriefingRef(MinisterName, briefing.BriefingVersion, $"willie:{briefing.BriefingVersion}"));

        IReadOnlyList<AgentFlag> flags = requests.Count > 0
            ? [new AgentFlag(
                Id: $"{Domain}:{trace}",
                SourceMinister: MinisterName,
                Severity: ToFlagSeverity(priority),
                Domain: Domain,
                Summary: title,
                BuildingRequests: requests.BuildingRequestsOrNull,
                LaborRequests: requests.LaborRequestsOrNull,
                ItemRequests: requests.ItemRequestsOrNull,
                Attention: requests.AttentionOrNull,
                Detail: trace,
                ExpiresAt: now.AddHours(24))]
            : [];

        RuleTraceDetails diagnostics = DiagnosticsFor(briefing, inboundBuildingRequests, trace)
            .WithEmissions("rules", trace, [advice], flags);
        return new Decision([advice], flags, trace, diagnostics);
    }

    private static WillieFlagRequests ItemRequests(
        IReadOnlyList<MaterialCount> materials,
        AdvicePriority priority)
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

    private static RuleTraceDetails DiagnosticsFor(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        string selectedRule)
    {
        IReadOnlyList<RuleTraceEntry> matches = RuleMatches(briefing, inboundBuildingRequests);
        RuleTraceEntry? selected = matches.FirstOrDefault(match =>
            string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<RuleTraceEntry> matchedSignals = selected is null
            ? [new RuleTraceEntry(selectedRule, "selected", "selected by fallthrough")]
            : [selected with { Outcome = "selected" }];
        IReadOnlyList<RuleTraceEntry> suppressed = matches
            .Where(match => !string.Equals(match.Rule, selectedRule, StringComparison.OrdinalIgnoreCase))
            .Select(match => match with { Outcome = "suppressed" })
            .ToList();

        return new RuleTraceDetails(selectedRule, matchedSignals, suppressed)
        {
            AllRules = AllRuleEvaluations(matches, selectedRule)
        };
    }

    private static IReadOnlyList<RuleEvaluationTrace> AllRuleEvaluations(
        IReadOnlyList<RuleTraceEntry> matches,
        string selectedRule)
    {
        Dictionary<string, RuleTraceEntry> outcomes = matches
            .GroupBy(match => match.Rule, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return
        [
            RuleEvaluation("power_net_deficit", outcomes, selectedRule, "has_power && net_w < 0", "place_blueprint"),
            RuleEvaluation("low_battery_reserve", outcomes, selectedRule, "has_power && capacity_wd > 0 && stored/capacity < 0.25", "place_blueprint"),
            RuleEvaluation("backlog_material_gap", outcomes, selectedRule, "missing_materials non-empty", "request_resource"),
            RuleEvaluation("frame_blocked_by_material", outcomes, selectedRule, "blocked_count > 0", "request_resource"),
            RuleEvaluation(BuildingRequestActiveTrace, outcomes, selectedRule, "inbound Willie building_request", "place_blueprint"),
            RuleEvaluation("kitchen_missing", outcomes, selectedRule, "room_counts lacks kitchen", "place_blueprint"),
            RuleEvaluation("hospital_missing", outcomes, selectedRule, "colonists >= 3 && room_counts lacks hospital", "place_blueprint"),
            RuleEvaluation("storage_room_missing", outcomes, selectedRule, "room_counts lacks storage && stockpile_zones == 0", "place_blueprint"),
            RuleEvaluation("cooler_missing", outcomes, selectedRule, "has_buildings && cooler_count == 0 && freezer_anchor_count > 0", "place_blueprint"),
            RuleEvaluation("maintain_build_program", outcomes, selectedRule, "no deterministic Willie rule matched", "none")
        ];
    }

    private static RuleEvaluationTrace RuleEvaluation(
        string rule,
        IReadOnlyDictionary<string, RuleTraceEntry> outcomes,
        string selectedRule,
        string conditions,
        string outputAction)
    {
        if (string.Equals(rule, selectedRule, StringComparison.OrdinalIgnoreCase))
            return new RuleEvaluationTrace(rule, "selected", conditions, outputAction, "selected by rule order");

        if (outcomes.TryGetValue(rule, out RuleTraceEntry? trace))
            return new RuleEvaluationTrace(rule, "matched", conditions, outputAction, trace.Reason);

        return new RuleEvaluationTrace(rule, "not_matched", conditions, outputAction, null);
    }

    private static IReadOnlyList<RuleTraceEntry> RuleMatches(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests)
    {
        List<RuleTraceEntry> matches = [];

        if (briefing.DataCoverage.HasPower && briefing.PowerStability.NetW < 0)
            matches.Add(new RuleTraceEntry("power_net_deficit", "matched", $"net_w={FormatWatts(briefing.PowerStability.NetW)}"));

        if (briefing.DataCoverage.HasPower && HasLowBatteryReserve(briefing.PowerStability))
            matches.Add(new RuleTraceEntry("low_battery_reserve", "matched", $"reserve={ReserveRatio(briefing.PowerStability):P0}"));

        if (briefing.MaterialBottleneck.MissingMaterials.Count > 0)
            matches.Add(new RuleTraceEntry("backlog_material_gap", "matched", FormatMaterials(briefing.MaterialBottleneck.MissingMaterials)));

        if (briefing.StalledBuilds.BlockedCount > 0)
            matches.Add(new RuleTraceEntry("frame_blocked_by_material", "matched", $"blocked_count={briefing.StalledBuilds.BlockedCount}"));

        if (HasFunctionalRoomEvidence(briefing) && MissingRoom(briefing, RoomClass.Kitchen))
            matches.Add(new RuleTraceEntry("kitchen_missing", "matched", "no kitchen room class anchor"));

        if (HasFunctionalRoomEvidence(briefing) && briefing.ColonistCount >= 3 && MissingRoom(briefing, RoomClass.Hospital))
            matches.Add(new RuleTraceEntry("hospital_missing", "matched", "no hospital room class anchor"));

        if (HasFunctionalRoomEvidence(briefing) &&
            MissingRoom(briefing, RoomClass.Storage) &&
            briefing.StoragePlacement.StockpileZones == 0)
        {
            matches.Add(new RuleTraceEntry("storage_room_missing", "matched", "no storage anchor or stockpile zone"));
        }

        int buildingRequests = inboundBuildingRequests.Count;
        if (buildingRequests > 0)
            matches.Add(new RuleTraceEntry(BuildingRequestActiveTrace, "matched", $"building_requests={buildingRequests}"));

        if (briefing.DataCoverage.HasBuildings &&
            briefing.ThermalControl.CoolerCount == 0 &&
            briefing.ThermalControl.FreezerAnchorCount > 0)
        {
            matches.Add(new RuleTraceEntry("cooler_missing", "matched", "freezer anchor exists without visible cooler"));
        }

        return matches;
    }

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
        string trace,
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> inboundBuildingRequests,
        out BuildingRequest request)
    {
        if (string.Equals(trace, BuildingRequestActiveTrace, StringComparison.OrdinalIgnoreCase))
        {
            request = SelectPlacementRequest(briefing, inboundBuildingRequests)!;
            return request is not null;
        }

        if (TryMissingRoomClass(trace, out RoomClass roomClass))
        {
            request = MissingRoomRequest(briefing, roomClass);
            return true;
        }

        request = null!;
        return false;
    }

    private static BuildingRequest? SelectPlacementRequest(
        WillieBriefing briefing,
        IReadOnlyList<BuildingRequest> requests) =>
        requests
            .OrderByDescending(request => MissingKitchenDependencyRank(briefing, request))
            .ThenByDescending(request => (int)(request.Priority ?? AdvicePriority.Medium))
            .ThenBy(request => FormatRequestTarget(request), StringComparer.OrdinalIgnoreCase)
            .ThenBy(request => request.Request, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static int MissingKitchenDependencyRank(WillieBriefing briefing, BuildingRequest request)
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

    private static bool TryMissingRoomClass(string trace, out RoomClass roomClass)
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

    private static BuildingRequest MissingRoomRequest(WillieBriefing briefing, RoomClass roomClass)
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

    private static AdvicePriority MissingRoomPriority(RoomClass roomClass) =>
        roomClass == RoomClass.Kitchen ? AdvicePriority.Medium : AdvicePriority.Low;

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

    private static FlagSeverity ToFlagSeverity(AdvicePriority priority) => priority switch
    {
        AdvicePriority.Critical => FlagSeverity.Critical,
        AdvicePriority.High => FlagSeverity.High,
        AdvicePriority.Medium => FlagSeverity.Medium,
        _ => FlagSeverity.Low
    };

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
