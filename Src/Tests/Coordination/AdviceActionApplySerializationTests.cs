using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using RimBob.Core.Advice;

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
}
