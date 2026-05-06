using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.State;
using RimAI.Tests.Infrastructure;

namespace RimAI.Tests.State;

/// <summary>
/// Verifies BriefingCache emits a structured log event on every cache miss, including
/// the full serialized briefing. Uses TestLogger so events go to both the captured
/// list (for assertion) and the shared test run log file.
/// </summary>
public sealed class BriefingCacheLoggingTests
{
    [Fact]
    public void GetMayorBriefing_OnCacheMiss_LogsVersionAggregatesAndFullBriefing()
    {
        var log = new TestLogger<BriefingCache>();
        var s = new ColonyState();
        var cache = new BriefingCache(s, log);

        // First call → cache miss
        cache.GetMayorBriefing();
        // Second call → cache hit, no log
        cache.GetMayorBriefing();
        // Bump Power → cache miss again
        s.Power.Update(new PowerNetwork(3000f, 1000f, 0f, 0f));
        cache.GetMayorBriefing();

        log.Events.Should().HaveCount(2, "only cache misses are logged");

        var (_, msg1) = log.Events[0];
        msg1.Should().Contain("version=1");
        msg1.Should().Contain("updatedAggregates=[initial]");   // first compute has no prior snapshot
        msg1.Should().Contain("briefing=");
        msg1.Should().Contain("\"BriefingVersion\":1");

        var (_, msg2) = log.Events[1];
        msg2.Should().Contain("version=2");
        msg2.Should().Contain("updatedAggregates=[Power]");     // only Power was bumped
        msg2.Should().Contain("\"BriefingVersion\":2");
        msg2.Should().Contain("\"NetW\":2000");                 // 3000 - 1000
    }
}
