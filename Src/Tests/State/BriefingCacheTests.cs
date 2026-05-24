using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.State;

public sealed class BriefingCacheTests
{
    [Fact]
    public void GetMayorBriefing_FirstCall_ReturnsFreshBriefing()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s, new TestLogger<BriefingCache>());

        var b = cache.GetMayorBriefing();

        b.Should().NotBeNull();
    }

    [Fact]
    public void GetMayorBriefing_NoVersionChange_ReturnsSameInstance()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s, new TestLogger<BriefingCache>());

        var first = cache.GetMayorBriefing();
        var second = cache.GetMayorBriefing();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetMayorBriefing_AfterAggregateUpdate_ReturnsNewInstance()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s, new TestLogger<BriefingCache>());

        var first = cache.GetMayorBriefing();

        s.Power.Update(new PowerNetwork(1000f, 500f, 0f, 0f));
        var second = cache.GetMayorBriefing();

        second.Should().NotBeSameAs(first);
        second.Power.NetW.Should().Be(500f);
        second.BriefingVersion.Should().Be(2);
    }

    [Fact]
    public void GetMayorBriefing_AfterEveryAggregateUpdates_RecomputesEachTime()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s, new TestLogger<BriefingCache>());

        // Touch each aggregate; each touch must invalidate the cache.
        var prev = cache.GetMayorBriefing();
        s.Map.Update(new MapInfoSnapshot(1, "small"));
        var afterMap = cache.GetMayorBriefing();
        afterMap.Should().NotBeSameAs(prev);

        s.Economy.Update(new EconomyLedger(1, 0, "", "", false, ""));
        cache.GetMayorBriefing().Should().NotBeSameAs(afterMap);
    }

    [Fact]
    public void GetFoodBriefing_NoVersionChange_ReturnsSameInstance()
    {
        ColonyState state = new();
        BriefingCache cache = new(state, new TestLogger<BriefingCache>());

        FoodBriefing first = cache.GetFoodBriefing();
        FoodBriefing second = cache.GetFoodBriefing();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetFoodBriefing_AfterAggregateUpdate_ReturnsNewInstance()
    {
        ColonyState state = new();
        BriefingCache cache = new(state, new TestLogger<BriefingCache>());

        FoodBriefing first = cache.GetFoodBriefing();
        state.Resources.Update(new ResourceSummary(
            TotalItems: 10,
            TotalMarketValue: 0f,
            FoodTotal: 6,
            TotalNutrition: 9.6f,
            MealsCount: 2,
            RawFoodCount: 4,
            MedicineTotal: 0,
            WeaponCount: 0,
            WeaponValue: 0f));
        FoodBriefing second = cache.GetFoodBriefing();

        second.Should().NotBeSameAs(first);
        second.BriefingVersion.Should().Be(2);
    }

    [Fact]
    public void GetWelfareBriefing_AfterRoomUpdate_ReturnsNewInstance()
    {
        ColonyState state = new();
        BriefingCache cache = new(state, new TestLogger<BriefingCache>());

        WelfareSourceBriefing first = cache.GetWelfareBriefing();
        state.Rooms.Update(new RoomRegistry([
            new RoomRecord(
                Id: "room-1",
                RoleLabel: "bedroom",
                Temperature: 21f,
                CellsCount: 16,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: ["bed-1"],
                Impressiveness: 31f,
                Beauty: 1f,
                Cleanliness: 0f,
                Space: 16f,
                Wealth: 400f)
        ]));
        WelfareSourceBriefing second = cache.GetWelfareBriefing();

        second.Should().NotBeSameAs(first);
        second.BriefingVersion.Should().Be(2);
        second.Rooms.Count.Should().Be(1);
    }
}
