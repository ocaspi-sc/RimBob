using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Host;
using RimBob.Ingestion;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.ApiHost;

public sealed class AssistedApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_WhenAdviceMissing_ReturnsStaleAdviceShape()
    {
        AssistedApplyService service = Service(new AdviceBus(), new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("missing", 0);

        response.Status.Should().Be("stale_advice");
        response.AdviceId.Should().Be("missing");
        response.ActionIndex.Should().Be(0);
        response.Kind.Should().BeNull();
        service.LatestAttempts().Should().ContainSingle().Which.Status.Should().Be("stale_advice");
    }

    [Fact]
    public async Task ApplyAsync_WhenAdviceExpiredByGameTick_ReturnsStaleWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark harvest.",
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "4 rice plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["plant-1", "plant-2", "plant-3", "plant-4"],
                TargetCount: 4))) with
        {
            Stamp = new AdviceStamp(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, 1_000, 2_000)
        });
        ColonyState state = new();
        state.Economy.Update(new EconomyLedger(2_000, 0, "Cassandra", "Playing", false, "5th of Aprimay, 5500, 14h"));
        AssistedApplyService service = Service(bus, state);

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("game tick 2000");
    }

    [Fact]
    public async Task ApplyAsync_WhenHarvestRectTooBroad_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark broad harvest.",
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "too broad",
                MapId: 1,
                Rect: new MapRect(0, 0, 20, 20),
                TargetIds: ["plant-1"],
                TargetCount: 1))));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("validation_failed");
        response.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        response.Message.Should().Contain("too broad");
    }

    [Fact]
    public void AssessHarvest_WhenForageTargetsAreHarvestableBelowGrowthThreshold_ReturnsReady()
    {
        ColonyState state = StateWithHarvestPlants(
            new PlantRecord("berry-1", "Plant_Berry", 0.90f, false, null, new MapPosition(10, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-2", "Plant_Berry", 0.45f, false, null, new MapPosition(11, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-3", "Plant_Berry", 0.45f, false, null, new MapPosition(12, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-4", "Plant_Berry", 0.45f, false, null, new MapPosition(13, 0, 20), IsHarvestable: true));

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(state, HarvestApply());

        assessment.Outcome.Should().Be(HarvestApplyOutcome.Ready);
        assessment.ReadyCount.Should().Be(4);
        assessment.MissingCount.Should().Be(0);
        assessment.StaleCount.Should().Be(0);
    }

    [Fact]
    public void AssessHarvest_WhenNoTargetsAreReady_ReturnsAlreadySatisfied()
    {
        ColonyState state = StateWithHarvestPlants(
            new PlantRecord("berry-1", "Plant_Berry", 0.45f, false, null, new MapPosition(10, 0, 20), IsHarvestable: false),
            new PlantRecord("berry-2", "Plant_Berry", 0.45f, false, null, new MapPosition(11, 0, 20), IsHarvestable: false),
            new PlantRecord("berry-3", "Plant_Berry", 0.45f, false, null, new MapPosition(12, 0, 20), IsHarvestable: false),
            new PlantRecord("berry-4", "Plant_Berry", 0.45f, false, null, new MapPosition(13, 0, 20), IsHarvestable: false));

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(state, HarvestApply());

        assessment.Outcome.Should().Be(HarvestApplyOutcome.AlreadySatisfied);
        assessment.ReadyCount.Should().Be(0);
        assessment.MissingCount.Should().Be(0);
        assessment.StaleCount.Should().Be(4);
    }

    [Fact]
    public void AssessHarvest_WhenTargetsAreMissingBeyondTolerance_ReturnsStaleMissing()
    {
        ColonyState state = StateWithHarvestPlants(
            new PlantRecord("berry-1", "Plant_Berry", 0.90f, false, null, new MapPosition(10, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-2", "Plant_Berry", 0.90f, false, null, new MapPosition(11, 0, 20), IsHarvestable: true));

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(state, HarvestApply());

        assessment.Outcome.Should().Be(HarvestApplyOutcome.StaleMissing);
        assessment.ReadyCount.Should().Be(2);
        assessment.MissingCount.Should().Be(2);
        assessment.StaleCount.Should().Be(2);
    }

    [Fact]
    public void AssessHarvest_WhenTargetsAreNotReadyBeyondTolerance_ReturnsStaleNotReady()
    {
        ColonyState state = StateWithHarvestPlants(
            new PlantRecord("berry-1", "Plant_Berry", 0.90f, false, null, new MapPosition(10, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-2", "Plant_Berry", 0.90f, false, null, new MapPosition(11, 0, 20), IsHarvestable: true),
            new PlantRecord("berry-3", "Plant_Berry", 0.45f, false, null, new MapPosition(12, 0, 20), IsHarvestable: false),
            new PlantRecord("berry-4", "Plant_Berry", 0.45f, false, null, new MapPosition(13, 0, 20), IsHarvestable: false));

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(state, HarvestApply());

        assessment.Outcome.Should().Be(HarvestApplyOutcome.StaleNotReady);
        assessment.ReadyCount.Should().Be(2);
        assessment.MissingCount.Should().Be(0);
        assessment.StaleCount.Should().Be(2);
    }

    [Fact]
    public void AssessHarvest_WhenAdviceTargetsDifferentMap_ReturnsWrongMap()
    {
        ColonyState state = StateWithHarvestPlants(
            new PlantRecord("berry-1", "Plant_Berry", 0.90f, false, null, new MapPosition(10, 0, 20), IsHarvestable: true));
        state.Map.Update(new MapInfoSnapshot(2, "test"));

        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(state, HarvestApply());

        assessment.Outcome.Should().Be(HarvestApplyOutcome.WrongMap);
        assessment.ReadyCount.Should().Be(0);
        assessment.MissingCount.Should().Be(0);
        assessment.StaleCount.Should().Be(0);
    }

    [Fact]
    public void AssessHarvest_WhenTargetAreaIsTooBroad_ReturnsTooBroad()
    {
        HarvestApplyAssessment assessment = AssistedApplyService.AssessHarvest(
            new ColonyState(),
            HarvestApply(rect: new MapRect(0, 0, 20, 20)));

        assessment.Outcome.Should().Be(HarvestApplyOutcome.TooBroad);
        assessment.ReadyCount.Should().Be(0);
        assessment.MissingCount.Should().Be(0);
        assessment.StaleCount.Should().Be(4);
    }

    [Fact]
    public async Task ApplyAsync_WhenMostHarvestTargetsAreNoLongerReady_ReturnsStaleWithoutPosting()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_harvest_mature_crops", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark harvest.",
            Apply: new MarkHarvestAreaApply(
                "Mark harvest",
                "4 rice plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["plant-1", "plant-2", "plant-3", "plant-4"],
                TargetCount: 4))));
        var handler = new MinimalRefreshHandler();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_harvest_mature_crops", 0);

        response.Status.Should().Be("stale_advice");
        handler.DesignatePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenForageTargetsAreHarvestableBelowGrowthThreshold_PostsHarvestDesignation()
    {
        // Forageable wild plants (berries, etc.) report is_harvestable=true well below 0.85 growth.
        // Generator selects them via IsHarvestable; the validator must agree, or "Apply Mark Forage"
        // wrongly reports stale. Here only 1 of 4 is >= 0.85 growth, so the old growth-only gate would
        // count 3 stale (> AllowedMissing(4)=1) and fail. PlantHarvest.IsReady honors is_harvestable.
        AdviceBus bus = new();
        bus.Publish(Advice("food_wild_harvest_available", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark forage.",
            Apply: new MarkHarvestAreaApply(
                "Mark forage",
                "4 berry plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["berry-1", "berry-2", "berry-3", "berry-4"],
                TargetCount: 4))));
        MinimalRefreshHandler handler = new()
        {
            MapPlantsJson = """
                {"success":true,"data":[
                  {"id":"berry-1","def":"Plant_Berry","growth":0.9,"is_crop":false,"is_harvestable":true,"position":{"x":10,"y":0,"z":20}},
                  {"id":"berry-2","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":true,"position":{"x":11,"y":0,"z":20}},
                  {"id":"berry-3","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":true,"position":{"x":12,"y":0,"z":20}},
                  {"id":"berry-4","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":true,"position":{"x":13,"y":0,"z":20}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_wild_harvest_available", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.MarkHarvestArea);
        response.Message.Should().Contain("Harvest designation");
        AssertAppliedAction(bus, "applied", AdviceApplyKind.MarkHarvestArea);
        handler.DesignatePosted.Should().BeTrue();
        handler.LastDesignateBody.Should().Contain("\"designation\":\"Harvest\"");
    }

    [Fact]
    public async Task ApplyAsync_WhenForageTargetsReportNotHarvestable_DoesNotPostHarvestDesignation()
    {
        // is_harvestable=false must still gate: a plant flagged not-harvestable is not ready even if
        // some other readiness heuristic might pass. All four are unready -> none ready in rect.
        AdviceBus bus = new();
        bus.Publish(Advice("food_wild_harvest_available", new AdviceAction(
            AdviceActionKind.MarkHarvest,
            "Mark forage.",
            Apply: new MarkHarvestAreaApply(
                "Mark forage",
                "4 berry plants",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 20),
                TargetIds: ["berry-1", "berry-2", "berry-3", "berry-4"],
                TargetCount: 4))));
        MinimalRefreshHandler handler = new()
        {
            MapPlantsJson = """
                {"success":true,"data":[
                  {"id":"berry-1","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":false,"position":{"x":10,"y":0,"z":20}},
                  {"id":"berry-2","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":false,"position":{"x":11,"y":0,"z":20}},
                  {"id":"berry-3","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":false,"position":{"x":12,"y":0,"z":20}},
                  {"id":"berry-4","def":"Plant_Berry","growth":0.45,"is_crop":false,"is_harvestable":false,"position":{"x":13,"y":0,"z":20}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_wild_harvest_available", 0);

        response.Status.Should().Be("already_satisfied");
        handler.DesignatePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntTargetsRemainLowRisk_PostsHuntThingDesignation()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = HandlerWithHares();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.MarkHuntArea);
        response.Message.Should().Contain("Hunt designation");
        AssertAppliedAction(bus, "applied", AdviceApplyKind.MarkHuntArea);
        handler.HuntPosted.Should().BeTrue();
        handler.DesignatePosted.Should().BeFalse();
        JsonDocument body = JsonDocument.Parse(handler.LastHuntBody);
        body.RootElement.GetProperty("map_id").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("thing_ids").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("hare-1", "hare-2");
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntRectContainsContaminants_PostsOnlyTargetAnimalIds()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}},
                  {"id":"hare-3","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}},
                  {"id":"wolf-1","def":"Wolf","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("applied");
        handler.HuntPosted.Should().BeTrue();
        handler.DesignatePosted.Should().BeFalse();
        handler.LastHuntBody.Should().Contain("hare-1");
        handler.LastHuntBody.Should().Contain("hare-2");
        handler.LastHuntBody.Should().NotContain("hare-3");
        handler.LastHuntBody.Should().NotContain("wolf-1");
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntTargetsChangedBeyondTolerance_ReturnsStaleWithoutPosting()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction(["hare-1", "hare-2", "hare-3", "hare-4"])));
        MinimalRefreshHandler handler = new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("Too many hunt targets changed");
        handler.HuntPosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenOneHuntTargetChangedWithinTolerance_PostsSurvivors()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction(["hare-1", "hare-2", "hare-3", "hare-4"])));
        MinimalRefreshHandler handler = new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}},
                  {"id":"hare-3","def":"Hare","tame":false,"health":1.0,"position":{"x":42,"y":0,"z":50}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("applied");
        handler.HuntPosted.Should().BeTrue();
        handler.LastHuntBody.Should().Contain("hare-1");
        handler.LastHuntBody.Should().Contain("hare-2");
        handler.LastHuntBody.Should().Contain("hare-3");
        handler.LastHuntBody.Should().NotContain("hare-4");
    }

    [Fact]
    public async Task ApplyAsync_WhenHuntTargetTurnsRisky_CountsAsChanged()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":0.4,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("stale_advice");
        handler.HuntPosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenAllHuntTargetsGone_ReturnsAlreadySatisfiedWithoutPosting()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_hunt_low_risk_animals", HuntAction()));
        MinimalRefreshHandler handler = new();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_hunt_low_risk_animals", 0);

        response.Status.Should().Be("already_satisfied");
        handler.HuntPosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillMissing_AddsBillAndReadsBack()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(403, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(403, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.UpsertProductionBill);
        response.Message.Should().Contain("created");
        AssertAppliedAction(bus, "applied", AdviceApplyKind.UpsertProductionBill);
        handler.AddBillPosted.Should().BeTrue();
        handler.UpdateBillPosted.Should().BeFalse();
        handler.LastBillWriteBody.Should().Contain("\"recipe_def_name\":\"CookMealSimple\"");
        handler.LastBillWriteBody.Should().Contain("\"target_count\":12");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillExistsWithDifferentTarget_UpdatesInsteadOfAdding()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 6)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 6)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("applied");
        response.Message.Should().Contain("updated");
        handler.AddBillPosted.Should().BeFalse();
        handler.UpdateBillPosted.Should().BeTrue();
        handler.LastBillWritePath.Should().Contain("bill_id=402");
        handler.LastBillWriteBody.Should().Contain("\"target_count\":12");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillAlreadySatisfied_DoesNotWrite()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        handler.EnqueueBillResponse(BillsJson(SimpleMealBillJson(402, 12)));
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("already_satisfied");
        AssertAppliedAction(bus, "already_satisfied", AdviceApplyKind.UpsertProductionBill);
        handler.AddBillPosted.Should().BeFalse();
        handler.UpdateBillPosted.Should().BeFalse();
        handler.BillListCalls.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillTargetOverCap_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction(AssistedApplyLimits.MaxProductionBillTarget + 1)));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("validation_failed");
        response.Message.Should().Contain("too high");
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillWorkbenchIsStale_ReturnsStaleWithoutBillCalls()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = new();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("stale_advice");
        handler.BillListCalls.Should().Be(0);
        handler.AddBillPosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenProductionBillReadbackDoesNotConfirm_ReturnsInconclusive()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_meals_understocked", ProductionBillAction()));
        MinimalRefreshHandler handler = HandlerWithSingleStove();
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        handler.EnqueueBillResponse(BillsJson());
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_meals_understocked", 0);

        response.Status.Should().Be("readback_inconclusive");
        handler.AddBillPosted.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupValidatesAndPlaces_AppliesAndRecordsResult()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction()));
        MinimalRefreshHandler handler = new();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("applied");
        response.Kind.Should().Be(AdviceApplyKind.PlaceBlueprintGroup);
        response.Message.Should().Contain("placed 1 asset");
        JsonSerializer.Serialize(response.Readback).Should().Contain("\"placed_count\":1");
        AssertAppliedAction(bus, "applied", AdviceApplyKind.PlaceBlueprintGroup);
        handler.BlueprintGroupValidateCalls.Should().Be(1);
        handler.BlueprintGroupPlacePosted.Should().BeTrue();
        handler.LastBlueprintGroupPlaceBody.Should().Contain("\"placement_order\":\"default\"");
        handler.LastBlueprintGroupPlaceBody.Should().Contain("\"require_all\":true");
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupValidateRejects_ReturnsStaleWithoutPlacing()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction()));
        MinimalRefreshHandler handler = new()
        {
            BlueprintGroupValidateJson = BlueprintGroupValidateResponseJson(
                canPlaceAll: false,
                canPlace: false,
                reason: "blocked by rock")
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("blocked by rock");
        handler.BlueprintGroupValidateCalls.Should().Be(1);
        handler.BlueprintGroupPlacePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupPlaceEndpointUnavailable_ReturnsRimApiUnavailable()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction()));
        MinimalRefreshHandler handler = new()
        {
            BlueprintGroupPlaceStatusCode = HttpStatusCode.NotFound
        };
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("rimapi_unavailable");
        response.Kind.Should().Be(AdviceApplyKind.PlaceBlueprintGroup);
        handler.BlueprintGroupValidateCalls.Should().Be(1);
        handler.BlueprintGroupPlacePosted.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupTargetsDifferentMap_ReturnsStaleWithoutValidating()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction(mapId: 9)));
        MinimalRefreshHandler handler = new();
        AssistedApplyService service = Service(bus, new ColonyState(), handler);

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("stale_advice");
        response.Message.Should().Contain("different map");
        handler.BlueprintGroupValidateCalls.Should().Be(0);
        handler.BlueprintGroupPlacePosted.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WhenBlueprintGroupExceedsCap_ReturnsValidationFailedWithoutRimApi()
    {
        AdviceBus bus = new();
        bus.Publish(Advice("food_freezer_missing", BlueprintGroupAction(assetCount: AssistedApplyLimits.MaxBlueprintGroupAssets + 1)));
        AssistedApplyService service = Service(bus, new ColonyState());

        AssistedApplyResponse response = await service.ApplyAsync("food_freezer_missing", 0);

        response.Status.Should().Be("validation_failed");
        response.Message.Should().Contain("too large");
    }

    private static AssistedApplyService Service(AdviceBus bus, ColonyState state)
    {
        return Service(bus, state, new ThrowingHandler());
    }

    private static AssistedApplyService Service(AdviceBus bus, ColonyState state, HttpMessageHandler handler)
    {
        RimApiClient rimApi = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8765/")
        });
        IngestionDispatcher ingestion = new(rimApi, state, new TestLogger<IngestionDispatcher>());
        RimApiPlacementProbe placementProbe = new(rimApi);
        return new AssistedApplyService(
            bus,
            state,
            ingestion,
            rimApi,
            placementProbe,
            placementProbe,
            new TestLogger<AssistedApplyService>());
    }

    private static AdviceItem Advice(string id, AdviceAction action)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new AdviceItem(
            Id: id,
            Minister: "Chef",
            Priority: Priority.High,
            Title: "Mature crops are ready",
            Body: "Body",
            Rationale: "Rationale",
            Actions: [action],
            GuideCitationIds: [],
            Stamp: new AdviceStamp(now, now.AddHours(1)));
    }

    private static void AssertAppliedAction(AdviceBus bus, string status, AdviceApplyKind kind)
    {
        AdviceAction action = bus.ActiveAdvice().Should().ContainSingle()
            .Which.Actions.Should().ContainSingle()
            .Subject;
        action.Apply.Should().BeNull();
        action.ApplyResult.Should().NotBeNull();
        action.ApplyResult!.Status.Should().Be(status);
        action.ApplyResult.Kind.Should().Be(kind);
        action.ApplyResult.Message.Should().NotBeNullOrWhiteSpace();
    }

    private static AdviceAction ProductionBillAction(int targetCount = 12, string workbenchId = "10") =>
        new(
            AdviceActionKind.ProductionBill,
            "Set/check simple meal bill.",
            Apply: new UpsertProductionBillApply(
                "Set simple meal bill",
                "simple meal bill on one cooking station",
                MapId: 1,
                WorkbenchBuildingId: workbenchId,
                RecipeSelectorKey: "simple_meal",
                RepeatMode: "TargetCount",
                TargetCount: targetCount));

    private static MarkHarvestAreaApply HarvestApply(
        IReadOnlyList<string>? targetIds = null,
        MapRect? rect = null,
        int mapId = 1)
    {
        IReadOnlyList<string> ids = targetIds ?? ["berry-1", "berry-2", "berry-3", "berry-4"];
        return new MarkHarvestAreaApply(
            "Mark forage",
            $"{ids.Count} berry plants",
            mapId,
            rect ?? new MapRect(10, 20, 13, 20),
            ids,
            ids.Count);
    }

    private static ColonyState StateWithHarvestPlants(params PlantRecord[] plants)
    {
        ColonyState state = new();
        state.Map.Update(new MapInfoSnapshot(1, "test"));
        state.Plants.Update(new PlantRegistry(plants));
        return state;
    }

    private static AdviceAction HuntAction(IReadOnlyList<string>? targetIds = null) =>
        new(
            AdviceActionKind.MarkHunt,
            $"Mark up to {targetIds?.Count ?? 2} hares for hunting.",
            Apply: new MarkHuntAreaApply(
                "Mark hunt",
                $"{targetIds?.Count ?? 2} hare hunt targets",
                MapId: 1,
                Rect: new MapRect(40, 50, 43, 50),
                TargetIds: targetIds ?? ["hare-1", "hare-2"],
                TargetCount: targetIds?.Count ?? 2));

    private static AdviceAction BlueprintGroupAction(int assetCount = 1, int mapId = 1)
    {
        IReadOnlyList<BlueprintAsset> assets = Enumerable.Range(0, assetCount)
            .Select(index => new BlueprintAsset(
                Role: "building",
                DefName: "Cooler",
                StuffDefName: "Steel",
                Cell: new MapCell(12 + index, 34),
                Rotation: 2))
            .ToList();

        return new(
            AdviceActionKind.PlaceBlueprint,
            "Review compact freezer placement.",
            Apply: new PlaceBlueprintGroupApply(
                Label: "Place freezer shell",
                TargetSummary: "Compact freezer shell with one cooler",
                MapId: mapId,
                BlueprintGroup: new BlueprintGroup(
                    Label: "Compact freezer",
                    MapId: mapId,
                    Assets: assets),
                AssetCount: assetCount));
    }

    private static MinimalRefreshHandler HandlerWithSingleStove() =>
        new()
        {
            MapBuildingsJson = """
                {"success":true,"data":[
                  {"id":10,"def":"FueledStove","label":"fueled stove","type":"Building_WorkTable","position":{"x":10,"y":0,"z":10}}
                ],"errors":null}
                """
        };

    private static MinimalRefreshHandler HandlerWithHares() =>
        new()
        {
            MapAnimalsJson = """
                {"success":true,"data":[
                  {"id":"hare-1","def":"Hare","tame":false,"health":1.0,"position":{"x":40,"y":0,"z":50}},
                  {"id":"hare-2","def":"Hare","tame":false,"health":1.0,"position":{"x":41,"y":0,"z":50}}
                ],"errors":null}
                """
        };

    private static string BillsJson(params string[] billJson) =>
        $$"""
          {"success":true,"data":[{{string.Join(",", billJson)}}],"errors":null}
          """;

    private static string SimpleMealBillJson(int billId, int targetCount) =>
        $$"""
          {"load_id":{{billId}},"recipe_def_name":"CookMealSimple","recipe_label":"cook simple meal","repeat_mode":"TargetCount","target_count":{{targetCount}},"suspended":false,"paused":false}
          """;

    private static string BlueprintGroupValidateResponseJson(
        bool canPlaceAll,
        bool canPlace = true,
        string? reason = null) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                can_place_all = canPlaceAll,
                items = new[]
                {
                    new
                    {
                        index = 0,
                        item = BlueprintGroupItemJson(),
                        can_place = canPlace,
                        reason,
                        def_type = "Building",
                        occupies_cells = new[] { new { x = 12, z = 34 } },
                        cost = new[] { new { def_name = "Steel", count = 90 } },
                        work_to_build = 300,
                        already_blueprinted = false,
                        already_built = false
                    }
                },
                cost = new[] { new { def_name = "Steel", count = 90 } },
                overlap_conflicts = Array.Empty<object>()
            },
            errors = (string[]?)null
        });

    private static string BlueprintGroupPlaceResponseJson(string status) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                status,
                require_all = true,
                placement_order = "default",
                items = new[]
                {
                    new
                    {
                        index = 0,
                        item = BlueprintGroupItemJson(),
                        status,
                        placed = status == "placed",
                        thing_id = 123,
                        reason = "",
                        validate = new
                        {
                            can_place = true,
                            reason = (string?)null,
                            def_type = "Building",
                            occupies_cells = new[] { new { x = 12, z = 34 } },
                            cost = new[] { new { def_name = "Steel", count = 90 } },
                            work_to_build = 300,
                            already_blueprinted = false,
                            already_built = false
                        }
                    }
                },
                cost = new[] { new { def_name = "Steel", count = 90 } },
                validate = new
                {
                    can_place_all = true,
                    items = Array.Empty<object>(),
                    cost = new[] { new { def_name = "Steel", count = 90 } },
                    overlap_conflicts = Array.Empty<object>()
                }
            },
            errors = (string[]?)null
        });

    private static object BlueprintGroupItemJson() =>
        new
        {
            role = "building",
            def_name = "Cooler",
            stuff_def_name = "Steel",
            cell = new { x = 12, z = 34 },
            rotation = 2
        };

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw new HttpRequestException("unexpected RIMAPI call", null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class MinimalRefreshHandler : HttpMessageHandler
    {
        private readonly Queue<string> _billResponses = [];

        public bool DesignatePosted { get; private set; }
        public bool HuntPosted { get; private set; }
        public bool AddBillPosted { get; private set; }
        public bool UpdateBillPosted { get; private set; }
        public bool BlueprintGroupPlacePosted { get; private set; }
        public int BillListCalls { get; private set; }
        public int BlueprintGroupValidateCalls { get; private set; }
        public string LastDesignateBody { get; private set; } = "";
        public string LastHuntBody { get; private set; } = "";
        public string LastBillWritePath { get; private set; } = "";
        public string LastBillWriteBody { get; private set; } = "";
        public string LastBlueprintGroupPlaceBody { get; private set; } = "";
        public HttpStatusCode BlueprintGroupValidateStatusCode { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode BlueprintGroupPlaceStatusCode { get; init; } = HttpStatusCode.OK;
        public string BlueprintGroupValidateJson { get; init; } = BlueprintGroupValidateResponseJson(canPlaceAll: true);
        public string BlueprintGroupPlaceJson { get; init; } = BlueprintGroupPlaceResponseJson("placed");
        public string MapAnimalsJson { get; init; } = """{"success":true,"data":[],"errors":null}""";
        public string MapBuildingsJson { get; init; } = """{"success":true,"data":[],"errors":null}""";
        public string MapPlantsJson { get; init; } = """
            {"success":true,"data":[
              {"id":"plant-1","def":"Plant_Rice","growth":0.9,"is_crop":true,"position":{"x":10,"y":0,"z":20}},
              {"id":"plant-2","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":11,"y":0,"z":20}},
              {"id":"plant-3","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":12,"y":0,"z":20}},
              {"id":"plant-4","def":"Plant_Rice","growth":0.4,"is_crop":true,"position":{"x":13,"y":0,"z":20}}
            ],"errors":null}
            """;
        public string RecipesJson { get; init; } = """
            {"success":true,"data":[
              {"def_name":"CookMealSimple","label":"cook simple meal","description":"Cook a simple meal.","work_amount":300,"work_skill":"Cooking","products":[{"thing_def":"MealSimple","count":1}],"ingredients":[]}
            ],"errors":null}
            """;

        public void EnqueueBillResponse(string json)
        {
            _billResponses.Enqueue(json);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("builder/blueprint-group/validate", StringComparison.OrdinalIgnoreCase))
            {
                BlueprintGroupValidateCalls++;
                return JsonResponse(BlueprintGroupValidateJson, BlueprintGroupValidateStatusCode);
            }

            if (path.Contains("builder/blueprint-group/place", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureBlueprintGroupPlaceAsync(request, ct);
            }

            if (path.Contains("order/designate/area", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureDesignateAsync(request, ct);
            }

            if (path.Contains("order/designate/hunt", StringComparison.OrdinalIgnoreCase))
            {
                return CaptureHuntAsync(request, ct);
            }

            if (path.Contains("buildings/bills/add", StringComparison.OrdinalIgnoreCase))
                return CaptureBillWriteAsync(request, isAdd: true, ct);
            if (path.Contains("buildings/bill/update", StringComparison.OrdinalIgnoreCase))
                return CaptureBillWriteAsync(request, isAdd: false, ct);
            if (path.Contains("buildings/recipes", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(RecipesJson);
            if (path.Contains("buildings/bills", StringComparison.OrdinalIgnoreCase))
            {
                BillListCalls++;
                string json = _billResponses.Count == 0
                    ? """{"success":true,"data":[],"errors":null}"""
                    : _billResponses.Dequeue();
                return JsonResponse(json);
            }

            if (path.Contains("maps", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[{"id":1,"index":0,"is_player_home":true,"is_pocket_map":false,"faction_id":"10","seed":1,"size":"250"}],"errors":null}""");
            if (path.Contains("game/state", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"game_tick":1000,"colony_wealth":0,"colonist_count":0,"storyteller":"Cassandra","is_paused":false,"program_state":"Playing","map_count":1},"errors":null}""");
            if (path.Contains("datetime", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"datetime":"5th of Aprimay, 5500, 14h"},"errors":null}""");
            if (path.Contains("colonists/detailed", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("map/farm/summary", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"total_crops":4,"avg_growth":0.5,"ready_to_harvest":1,"crop_breakdown":[{"def":"Plant_Rice","count":4,"avg_growth":0.5}]},"errors":null}""");
            if (path.Contains("map/plants", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(MapPlantsJson);
            if (path.Contains("map/things", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("map/work-tables", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("def/all", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"things_defs":[],"terrain_defs":[]},"errors":null}""");
            if (path.Contains("resources/stored", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            if (path.Contains("map/animals", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(MapAnimalsJson);
            if (path.Contains("map/rooms", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"rooms":[]},"errors":null}""");
            if (path.Contains("map/zones", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{},"errors":null}""");
            if (path.Contains("map/terrain", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"width":0,"height":0,"palette":[],"grid":[]},"errors":null}""");
            if (path.Contains("map/buildings", StringComparison.OrdinalIgnoreCase))
                return JsonResponse(MapBuildingsJson);
            if (path.Contains("map/power/info", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"production":0,"consumption":0,"stored":0,"capacity":0},"errors":null}""");
            if (path.Contains("map/weather", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"def":"Clear","temperature":21,"rain_rate":0},"errors":null}""");
            if (path.Contains("lords", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":[],"errors":null}""");
            if (path.Contains("incidents", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"incidents":[]},"errors":null}""");
            if (path.Contains("resources/summary", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"total_items":0,"total_market_value":0,"critical_resources":{"food_summary":{"food_total":0,"total_nutrition":0,"meals_count":0,"raw_food_count":0},"medicine_total":0,"weapon_count":0,"weapon_value":0}},"errors":null}""");
            if (path.Contains("research/progress", StringComparison.OrdinalIgnoreCase))
                return JsonResponse("""{"success":true,"data":{"name":"none","label":"None","progress":0,"research_points":0,"is_finished":false,"can_start_now":false,"progress_percent":0},"errors":null}""");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private async Task<HttpResponseMessage> CaptureBlueprintGroupPlaceAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            BlueprintGroupPlacePosted = true;
            LastBlueprintGroupPlaceBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(BlueprintGroupPlaceStatusCode)
            {
                Content = new StringContent(BlueprintGroupPlaceJson, Encoding.UTF8, "application/json")
            };
        }

        private async Task<HttpResponseMessage> CaptureDesignateAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            DesignatePosted = true;
            LastDesignateBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{},"errors":null}""", Encoding.UTF8, "application/json")
            };
        }

        private async Task<HttpResponseMessage> CaptureHuntAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            HuntPosted = true;
            LastHuntBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{},"errors":null}""", Encoding.UTF8, "application/json")
            };
        }

        private async Task<HttpResponseMessage> CaptureBillWriteAsync(
            HttpRequestMessage request,
            bool isAdd,
            CancellationToken ct)
        {
            if (isAdd)
                AddBillPosted = true;
            else
                UpdateBillPosted = true;

            LastBillWritePath = request.RequestUri?.PathAndQuery ?? "";
            LastBillWriteBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{},"errors":null}""", Encoding.UTF8, "application/json")
            };
        }

        private static Task<HttpResponseMessage> JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
