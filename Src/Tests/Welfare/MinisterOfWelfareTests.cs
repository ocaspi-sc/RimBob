using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Ministers;
using RimBob.Ministers.Welfare;
using RimBob.State;
using RimBob.Tests.Infrastructure;

namespace RimBob.Tests.Welfare;

public sealed class MinisterOfWelfareTests
{
    private static readonly DateTimeOffset FixedNow = new(2036, 6, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RuleDecision_PublishesAdviceSnapshotFlagAndReplay()
    {
        CapturingReplayWriter replay = new();
        Harness harness = new(replay);
        harness.SetNewColonyState();

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualRulesOnly, CancellationToken.None);

        AdviceItem advice = harness.Bus.ActiveAdvice().Should().ContainSingle().Subject;
        advice.Minister.Should().Be("Welfare");
        advice.Id.Should().Be("welfare_shelter_floor");
        harness.Bus.ActiveSnapshot().StateSummaries.Should().ContainKey("Welfare")
            .WhoseValue.Should().Contain("Shelter:");
        AgentFlag flag = harness.Flags.Active().Should().ContainSingle().Subject;
        flag.Id.Should().Be("welfare:shelter_floor");
        harness.OutputStore.GetAdviceSnapshot("Welfare")!.Advice.Should().ContainSingle()
            .Which.Id.Should().Be("welfare_shelter_floor");
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.Minister.Should().Be("Welfare");
        record.RuleTrace.Should().Be("shelter_floor");
        record.Flags.Should().ContainSingle().Which.Id.Should().Be("welfare:shelter_floor");
    }

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public MinisterOutputStore OutputStore { get; } = new();
        public AdviceBus Bus { get; }
        public FlagChannel Flags { get; } = new();
        public BriefingCache Cache { get; }
        public MinisterOfWelfare Minister { get; }

        public Harness(IReplayCorpusWriter replay)
        {
            Bus = new AdviceBus(OutputStore);
            Cache = new BriefingCache(Colony, new TestLogger<BriefingCache>());
            Minister = new(
                Cache,
                new Rules(new FixedTimeProvider(FixedNow)),
                OutputStore,
                Bus,
                Flags,
                NullLogger<MinisterOfWelfare>.Instance,
                new MinisterReplayRecorder(replay));
        }

        public void SetNewColonyState()
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = FixedNow;
            Colony.Economy.Update(new EconomyLedger(120_000, 0f, "Cassandra", "Playing", false, ""));
            Colony.Colonists.Update(new ColonistRegistry(
            [
                Colonist("p1", "Alice", 0.62f),
                Colonist("p2", "Bob", 0.74f),
                Colonist("p3", "Cora", 0.8f)
            ]));
            Colony.Rooms.Update(new RoomRegistry([]));
        }

        private static ColonistRecord Colonist(string id, string name, float mood) =>
            new(
                Id: id,
                Name: name,
                Age: 30,
                Gender: "Unknown",
                Health: 1f,
                Mood: mood,
                Hunger: 0.8f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [],
                Traits: [],
                Sleep: 0.7f,
                Comfort: 0.7f,
                Beauty: 0.6f,
                Joy: 0.7f,
                FreshAir: 0.7f,
                DrugsDesire: 0f);
    }

    private sealed class CapturingReplayWriter : IReplayCorpusWriter
    {
        public List<MinisterReplayRecord> Records { get; } = [];

        public Task WriteAsync(MinisterReplayRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
