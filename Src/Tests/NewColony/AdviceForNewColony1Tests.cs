using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Core.Placement;
using RimBob.Host;
using RimBob.Ministers.Willie;
using RimBob.State;
using RimBob.Tests.Infrastructure;
using FoodRules = RimBob.Ministers.Food.Rules;
using WelfareRules = RimBob.Ministers.Welfare.Rules;
using WillieRules = RimBob.Ministers.Willie.Rules;

namespace RimBob.Tests.NewColony;

public sealed class AdviceForNewColony1Tests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<Task<NewColonyScenario>> Scenario = new(LoadScenarioAsync);

    [Fact]
    public async Task Snapshot_Loads_And_DerivesThreeBriefings()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.Snapshot.SchemaVersion.Should().Be(ColonyStateSnapshot.CurrentSchemaVersion);
        scenario.Snapshot.Source.Should().Be("live");
        scenario.Snapshot.GameTick.Should().Be(538);
        scenario.Snapshot.GameDate.ColonyDay.Should().Be(1);
        scenario.State.LastRefreshSource.Should().Be(ColonyStateOrigin.Snapshot);
        scenario.State.Colonists.Value.Colonists.Should().HaveCount(3);

        scenario.Food.UnforbidTargets.Should().Contain(target =>
            target.Def == "RawFungus" &&
            target.Count == 49 &&
            target.Kind == "raw_food");

        scenario.Welfare.Sleep.BedDeficit.Should().Be(3);
        scenario.Welfare.Rooms.BedroomCount.Should().Be(0);
        scenario.Welfare.ThoughtDigest.ByCategory.Should().Contain(group =>
            group.Category == ThoughtCategory.Temperature &&
            group.PawnCount == 2 &&
            group.WorstOffset <= -4f);

        scenario.Willie.FunctionalRooms.RoomCountsByClass.Should().BeEmpty();
    }

    [Fact]
    // REACH: target food-buffer accounting (~10.7d, 57 meals, 0 raw); RED until nutrition derivation matches - see .plans/advice-for-new-colony-1.md
    [Trait("kind", "reach")]
    public async Task Snapshot_DerivesTargetFoodBuffer()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.Food.NutritionSource.Should().Be("item_def_catalog");
        scenario.Food.EstimatedDaysOfFood.Should().NotBeNull();
        scenario.Food.EstimatedDaysOfFood!.Value.Should().BeApproximately(10.7f, 0.2f);
        scenario.Food.MealsCount.Should().Be(57);
        scenario.Food.RawFoodCount.Should().Be(0);
    }

    [Fact]
    public async Task Chef_NewColony1_DesignatesFoodStockpile()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.FoodDecision.Advice.SelectMany(advice => advice.Actions)
            .Should().Contain(action =>
                action.Kind == AdviceActionKind.SetStockpileZone &&
                action.Owner == "Willie");
        WillieBuildingRequests(scenario.FoodDecision).Should().Contain(request =>
            request.TargetClass == BuildingClass.Stockpile &&
            request.RequestedFrom == "Willie");
    }

    [Fact]
    public async Task Chef_NewColony1_RequestsStarterKitchen()
    {
        NewColonyScenario scenario = await Scenario.Value;

        WillieBuildingRequests(scenario.FoodDecision).Should().Contain(request =>
            request.TargetClass == BuildingClass.ProductionBench &&
            request.RoomClass == RoomClass.Kitchen &&
            request.RequestedFrom == "Willie");
    }

    [Fact]
    public async Task Chef_NewColony1_UnforbidsForbiddenFungus()
    {
        NewColonyScenario scenario = await Scenario.Value;

        AdviceAction unforbid = scenario.FoodDecision.Advice.SelectMany(advice => advice.Actions)
            .Should().Contain(action =>
                action.Kind == AdviceActionKind.Unforbid &&
                action.Apply is UnforbidThingsApply)
            .Subject;
        UnforbidThingsApply apply = unforbid.Apply.Should().BeOfType<UnforbidThingsApply>().Subject;
        apply.ThingTargets.Should().Contain(target => target.Def == "RawFungus");
        scenario.FoodDecision.Flags.SelectMany(flag => flag.ItemRequests ?? [])
            .Should().Contain(request => request.ItemDef == "RawFungus" && request.Quantity == 49);
    }

    [Fact]
    public async Task Chef_NewColony1_ForagesNearestBerries()
    {
        NewColonyScenario scenario = await Scenario.Value;

        AdviceAction action = HarvestActionByAdviceId(scenario.FoodDecision, "chef_wild_harvest_available");
        action.Instruction.Should().Contain("Plant_Berry");
        MarkHarvestAreaApply apply = action.Apply.Should().BeOfType<MarkHarvestAreaApply>().Subject;
        apply.TargetIds.Should().NotBeEmpty();
        apply.TargetCount.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task Chef_NewColony1_ForageApplyVerdictIsReady()
    {
        NewColonyScenario scenario = await Scenario.Value;

        AdviceAction action = HarvestActionByAdviceId(scenario.FoodDecision, "chef_wild_harvest_available");
        MarkHarvestAreaApply apply = action.Apply.Should().BeOfType<MarkHarvestAreaApply>().Subject;

        // Precondition: at least one emitted target must be harvestable while below the growth
        // threshold the old apply predicate gated on - the ebfaef5 divergence zone. Above the
        // threshold the generator and apply readiness predicates agree even when drifted apart, so
        // without a target in this zone the assertions below would still pass with the bug
        // reintroduced, and this regression guard would be silently toothless (e.g. after a
        // snapshot recapture that no longer contains harvestable-below-0.85 forage).
        HashSet<string> targetIds = apply.TargetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        scenario.State.Plants.Value.Plants
            .Where(plant => targetIds.Contains(plant.Id))
            .Should().Contain(
                plant => plant.IsHarvestable == true && plant.Growth < PlantHarvest.DefaultHarvestMinGrowth,
                "the new-colony fixture must exercise the harvestable-below-growth-threshold rule, "
                + "otherwise this verdict test cannot catch generator/apply predicate drift");

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(scenario.State, apply);

        // Generator and apply now share PlantHarvest.IsReady, so every selected target must read
        // back as apply-ready: ReadyCount == target count is the precise "no drift" invariant (it
        // also subsumes MissingCount == 0 and StaleCount == 0). Reverting the shared predicate drops
        // the below-threshold targets guaranteed by the precondition above, lowering ReadyCount.
        assessment.Outcome.Should().Be(HarvestApplyOutcome.Ready);
        assessment.ReadyCount.Should().Be(apply.TargetIds.Count);
    }

    [Fact]
    // REACH: demote expand_growing_capacity to <= Medium; RED until the "emergency rice" framing is dropped at a healthy buffer - see .plans/advice-for-new-colony-1.md
    [Trait("kind", "reach")]
    public async Task Chef_NewColony1_GrowingCapacityIsNotHigh()
    {
        NewColonyScenario scenario = await Scenario.Value;

        AdviceItem advice = AdviceById(scenario.FoodDecision, "chef_expand_growing_capacity");

        ((int)advice.Priority).Should().BeLessThanOrEqualTo((int)Priority.Medium);
    }

    [Fact]
    // REACH: no far-hunt + premature butcher on day 1; RED until hunt is demoted/suppressed at a healthy buffer - see .plans/advice-for-new-colony-1.md
    [Trait("kind", "reach")]
    public async Task Chef_NewColony1_DoesNotPushFarHunt()
    {
        NewColonyScenario scenario = await Scenario.Value;

        AdviceItem? hunt = scenario.FoodDecision.Advice.SingleOrDefault(advice => advice.Id == "chef_hunt_low_risk_animals");
        if (hunt is not null)
            hunt.Priority.Should().Be(Priority.Low);

        WillieBuildingRequests(scenario.FoodDecision).Should().NotContain(request =>
            request.RoomClass == RoomClass.Butcher &&
            request.TargetClass == BuildingClass.ProductionBench);
    }

    [Fact]
    public async Task Welfare_NewColony1_EmitsShelter()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.WelfareDecision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "shelter_floor" &&
            row.Outcome == RuleOutcome.Selected);
        scenario.WelfareDecision.Advice.Should().Contain(advice => advice.Id == "welfare_shelter_floor");
        WillieBuildingRequests(scenario.WelfareDecision).Should().Contain(request =>
            request.TargetClass == BuildingClass.Bed &&
            request.TargetDef == "Bed" &&
            request.RoomClass == RoomClass.Barracks &&
            request.CapacityNeed == new CapacityNeed(CapacityMeasure.Beds, 3, null));
    }

    [Fact]
    public async Task Welfare_NewColony1_EmitsTemperature()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.WelfareDecision.Trace.Should().Be("rules:shelter_floor+temperature_comfort");
        scenario.WelfareDecision.Advice.Should().Contain(advice => advice.Id == "welfare_temperature_comfort");
        WillieBuildingRequests(scenario.WelfareDecision).Should().Contain(request =>
            request.TargetClass == BuildingClass.Heater &&
            request.TargetDef == "Heater" &&
            request.RoomClass == RoomClass.Barracks);
    }

    [Fact]
    public async Task Welfare_NewColony1_DoesNotEscalateHealthThoughts()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.WelfareRaw.Decisions.OfType<Escalate>().Should().BeEmpty();
    }

    [Fact]
    // REACH: standalone (snapshot-mode) Willie flags kitchen_missing; RED until that rule path covers it - see .plans/advice-for-new-colony-1.md
    [Trait("kind", "reach")]
    public async Task Willie_NewColony1_Standalone_FlagsMissingKitchen()
    {
        NewColonyScenario scenario = await Scenario.Value;

        scenario.WillieStandaloneDecision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == "kitchen_missing" &&
            row.Outcome == RuleOutcome.Selected);
        scenario.WillieStandaloneDecision.Advice.Should().Contain(advice => advice.Id == "willie_kitchen_missing");
    }

    [Fact]
    // REACH: solver places the High-priority beds request; RED until the bed-anchor no-fit is fixed - see .plans/advice-for-new-colony-1.md
    [Trait("kind", "reach")]
    public async Task Willie_NewColony1_PlacesHighPriorityBeds()
    {
        NewColonyScenario scenario = await Scenario.Value;
        IReadOnlyList<BuildingRequest> inbound = WillieBuildingRequests(
            scenario.WelfareDecision,
            scenario.FoodDecision);

        ProjectedRuleRun decision = Project(
            new WillieRules(new FixedTimeProvider(FixedNow)).Evaluate(scenario.Willie, inbound),
            scenario.Willie);

        decision.Diagnostics.AllRules.Should().Contain(row =>
            row.Rule == WillieRules.BuildingRequestActiveTrace &&
            row.Outcome == RuleOutcome.Selected);
        WillieRules.TryGetPlacementRequest(WillieRules.BuildingRequestActiveTrace, scenario.Willie, inbound, out BuildingRequest request)
            .Should().BeTrue();
        request.TargetClass.Should().Be(BuildingClass.Bed);
        request.RoomClass.Should().Be(RoomClass.Barracks);
        request.Priority.Should().Be(Priority.High);

        PlacementSolver solver = new(new AlwaysReachablePathCostProbe(), new AcceptingPlacementValidator());
        PlacementResult result = await solver.SolveAsync(
            PlacementSpec.FromBuildingRequest(request),
            scenario.Willie,
            scenario.State);

        result.NoFit.Should().BeNull();
        result.Options.Should().NotBeEmpty();
    }

    private static async Task<NewColonyScenario> LoadScenarioAsync()
    {
        string fixturePath = FindFixturePath("new-colony-1.colony-state.json");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "rimbob-new-colony-tests", Guid.NewGuid().ToString("N"));
        string tempPath = Path.Combine(tempDirectory, "new-colony-1.colony-state.json");
        Directory.CreateDirectory(tempDirectory);
        File.Copy(fixturePath, tempPath);

        try
        {
            ColonyStateSnapshotStore store = await ColonyStateSnapshotStore.LoadAsync(
                tempPath,
                new TestLogger<ColonyStateSnapshotStore>());
            if (store.Latest is null)
            {
                ColonySnapshotStatus status = store.GetStatus();
                throw new InvalidOperationException(
                    $"Could not load new-colony-1 snapshot fixture from {fixturePath}. Run git lfs pull, or recapture the fixture if the snapshot schema changed. Load error: {status.LoadError ?? "fixture missing or empty"}");
            }

            ColonyState state = new();
            store.RestoreInto(state);
            BriefingCache cache = new(state, new TestLogger<BriefingCache>());
            FoodBriefing food = cache.GetFoodBriefing();
            WelfareSourceBriefing welfare = cache.GetWelfareBriefing();
            WillieBriefing willie = cache.GetWillieBriefing();

            RuleRun foodRaw = new FoodRules().Evaluate(food);
            RuleRun welfareRaw = new WelfareRules(new FixedTimeProvider(FixedNow)).Evaluate(welfare);
            RuleRun willieStandaloneRaw = new WillieRules(new FixedTimeProvider(FixedNow)).Evaluate(willie);

            return new NewColonyScenario(
                state,
                store.Latest,
                food,
                welfare,
                willie,
                foodRaw.ProjectFor("Chef", "food", food.BriefingVersion, food.Date, food.GameTick, FixedNow),
                welfareRaw.ProjectFor("Welfare", "welfare", welfare.BriefingVersion, null, welfare.GameTick, FixedNow),
                welfareRaw,
                Project(willieStandaloneRaw, willie));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static ProjectedRuleRun Project(RuleRun run, WillieBriefing briefing) =>
        run.ProjectFor("Willie", "construction", briefing.BriefingVersion, briefing.Date, briefing.GameTick, FixedNow);

    private static AdviceItem AdviceById(ProjectedRuleRun decision, string id) =>
        decision.Advice.Should().ContainSingle(advice => advice.Id == id).Subject;

    private static AdviceAction HarvestActionByAdviceId(ProjectedRuleRun decision, string id) =>
        AdviceById(decision, id).Actions.Should().ContainSingle(action => action.Kind == AdviceActionKind.MarkHarvest).Subject;

    private static IReadOnlyList<BuildingRequest> WillieBuildingRequests(params ProjectedRuleRun[] decisions) =>
        decisions
            .SelectMany(decision => decision.Flags)
            .SelectMany(flag => flag.BuildingRequests ?? [])
            .Where(request => string.Equals(request.RequestedFrom, "Willie", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string FindFixturePath(string fileName)
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Src", "Tests", "NewColony", "Fixtures", fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find NewColony fixture {fileName}.");
    }

    private sealed record NewColonyScenario(
        ColonyState State,
        ColonyStateSnapshot Snapshot,
        FoodBriefing Food,
        WelfareSourceBriefing Welfare,
        WillieBriefing Willie,
        ProjectedRuleRun FoodDecision,
        ProjectedRuleRun WelfareDecision,
        RuleRun WelfareRaw,
        ProjectedRuleRun WillieStandaloneDecision);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class AlwaysReachablePathCostProbe : IPathCostProbe
    {
        public Task<IReadOnlyList<PathCostResult>> GetPathCostsAsync(
            int mapId,
            IReadOnlyList<PathCostPair> pairs,
            string tier = "region",
            string mode = "pass_doors",
            string peMode = "on_cell",
            CancellationToken ct = default)
        {
            IReadOnlyList<PathCostResult> results = pairs
                .Select(pair => new PathCostResult(true, 8, pair.From, pair.To))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class AcceptingPlacementValidator : IPlacementValidator
    {
        public Task<PlacementValidationResult> ValidateAsync(
            BlueprintGroup group,
            CancellationToken ct = default)
        {
            PlacementValidationResult result = new(
                CanPlaceAll: true,
                Items: [],
                Cost: [new MaterialEstimate("WoodLog", 60)],
                OverlapConflicts: []);
            return Task.FromResult(result);
        }
    }
}
