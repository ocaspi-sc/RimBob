using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;

namespace RimBob.Tests.Coordination;

public sealed class AdviceActionApplySerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void PlaceBlueprintGroupApply_SerializesAndRoundTrips()
    {
        AdviceAction action = new(
            AdviceActionKind.PlaceBlueprint,
            "Review compact freezer placement.",
            Apply: new PlaceBlueprintGroupApply(
                Label: "Place freezer shell",
                TargetSummary: "Compact freezer shell with one cooler",
                MapId: 1,
                BlueprintGroup: new BlueprintGroup(
                    Label: "Compact freezer",
                    MapId: 1,
                    Assets:
                    [
                        new BlueprintAsset(
                            Role: "building",
                            DefName: "Cooler",
                            StuffDefName: "Steel",
                            Cell: new MapCell(12, 34),
                            Rotation: 2)
                    ]),
                AssetCount: 1));

        string json = JsonSerializer.Serialize(action, JsonOptions);
        AdviceAction? roundTripped = JsonSerializer.Deserialize<AdviceAction>(json, JsonOptions);

        json.Should().Contain("\"kind\":\"place_blueprint_group\"");
        json.Should().Contain("\"blueprint_group\"");
        roundTripped.Should().NotBeNull();
        PlaceBlueprintGroupApply apply = roundTripped!.Apply.Should().BeOfType<PlaceBlueprintGroupApply>().Subject;
        apply.Kind.Should().Be(AdviceApplyKind.PlaceBlueprintGroup);
        apply.AssetCount.Should().Be(1);
        apply.BlueprintGroup.Assets.Should().ContainSingle().Which.DefName.Should().Be("Cooler");
    }

    [Fact]
    public void CreateGrowingZoneApply_SerializesAndRoundTrips()
    {
        AdviceAction action = new(
            AdviceActionKind.DesignateZoneReq,
            "Create the rice growing zone.",
            Apply: new CreateGrowingZoneApply(
                Label: "Growing zone 10,20",
                TargetSummary: "36-tile Plant_Rice growing zone at 10,20-15,25.",
                MapId: 1,
                PlantDef: "Plant_Rice",
                Rect: new MapRect(10, 20, 15, 25),
                TargetCount: 36));

        string json = JsonSerializer.Serialize(action, JsonOptions);
        AdviceAction? roundTripped = JsonSerializer.Deserialize<AdviceAction>(json, JsonOptions);

        json.Should().Contain("\"kind\":\"designate_zone_req\"");
        json.Should().Contain("\"kind\":\"create_growing_zone\"");
        json.Should().Contain("\"plant_def\":\"Plant_Rice\"");
        json.Should().Contain("\"target_count\":36");
        roundTripped.Should().NotBeNull();
        CreateGrowingZoneApply apply = roundTripped!.Apply.Should().BeOfType<CreateGrowingZoneApply>().Subject;
        apply.Kind.Should().Be(AdviceApplyKind.CreateGrowingZone);
        apply.Rect.Area.Should().Be(36);
        apply.PlantDef.Should().Be("Plant_Rice");
    }

    [Fact]
    public void CreateStockpileZoneApply_SerializesAndRoundTrips()
    {
        AdviceAction action = new(
            AdviceActionKind.SetStockpileZone,
            "Create the food stockpile zone.",
            Apply: new CreateStockpileZoneApply(
                Label: "Stockpile zone 10,20",
                TargetSummary: "12-tile stockpile zone for Foods at 10,20-13,22.",
                MapId: 1,
                Rect: new MapRect(10, 20, 13, 22),
                TargetCount: 12,
                Name: "Food stockpile",
                Priority: 0,
                AllowedItemCategories: ["Foods"]));

        string json = JsonSerializer.Serialize(action, JsonOptions);
        AdviceAction? roundTripped = JsonSerializer.Deserialize<AdviceAction>(json, JsonOptions);

        json.Should().Contain("\"kind\":\"set_stockpile_zone\"");
        json.Should().Contain("\"kind\":\"create_stockpile_zone\"");
        json.Should().Contain("\"allowed_item_categories\":[\"Foods\"]");
        json.Should().Contain("\"target_count\":12");
        roundTripped.Should().NotBeNull();
        CreateStockpileZoneApply apply = roundTripped!.Apply.Should().BeOfType<CreateStockpileZoneApply>().Subject;
        apply.Kind.Should().Be(AdviceApplyKind.CreateStockpileZone);
        apply.Rect.Area.Should().Be(12);
        apply.AllowedItemCategories.Should().ContainSingle().Which.Should().Be("Foods");
    }
}
