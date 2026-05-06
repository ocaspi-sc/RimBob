using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.State;

namespace RimAI.Tests.State;

public sealed class BriefingCacheTests
{
    [Fact]
    public void GetMayorBriefing_FirstCall_ReturnsFreshBriefing()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s);

        var b = cache.GetMayorBriefing();

        b.Should().NotBeNull();
    }

    [Fact]
    public void GetMayorBriefing_NoVersionChange_ReturnsSameInstance()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s);

        var first = cache.GetMayorBriefing();
        var second = cache.GetMayorBriefing();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetMayorBriefing_AfterAggregateUpdate_ReturnsNewInstance()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s);

        var first = cache.GetMayorBriefing();

        s.Power.Update(new PowerNetwork(1000f, 500f, 0f, 0f));
        var second = cache.GetMayorBriefing();

        second.Should().NotBeSameAs(first);
        second.Power.NetW.Should().Be(500f);
    }

    [Fact]
    public void GetMayorBriefing_AfterEveryAggregateUpdates_RecomputesEachTime()
    {
        var s = new ColonyState();
        var cache = new BriefingCache(s);

        // Touch each aggregate; each touch must invalidate the cache.
        var prev = cache.GetMayorBriefing();
        s.Map.Update(new MapInfoSnapshot(1, "small"));
        var afterMap = cache.GetMayorBriefing();
        afterMap.Should().NotBeSameAs(prev);

        s.Economy.Update(new EconomyLedger(1, 0, "", "", false, ""));
        cache.GetMayorBriefing().Should().NotBeSameAs(afterMap);
    }
}
