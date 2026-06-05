using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
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

    [Fact]
    public async Task StartupBootstrap_DoesNotForceLlm()
    {
        int calls = 0;
        Harness harness = new(executor: (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new WelfareLlmResponse([], []));
        });
        harness.SetNewColonyState();

        await harness.Minister.RunPlayCycle(PlayCycleContext.StartupBootstrap, CancellationToken.None);

        calls.Should().Be(0);
        harness.Bus.ActiveAdvice().Should().ContainSingle()
            .Which.Id.Should().Be("welfare_shelter_floor");
    }

    [Fact]
    public async Task ManualForceLlm_CallsLlmEvenWhenRulesWouldDecide()
    {
        int calls = 0;
        Harness harness = new(executor: (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new WelfareLlmResponse([WelfareAdvice("forced_welfare_llm")], []));
        });
        harness.SetNewColonyState();

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualForceLlm, CancellationToken.None);

        calls.Should().Be(1);
        harness.Bus.ActiveAdvice().Should().ContainSingle()
            .Which.Id.Should().Be("forced_welfare_llm");
    }

    [Fact]
    public async Task ManualRulesOnly_WhenRulesEscalate_DoesNotCallLlm()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        Harness harness = new(replay, (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new WelfareLlmResponse([WelfareAdvice("llm_welfare")], []));
        });
        harness.SetSocialPressureState();

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualRulesOnly, CancellationToken.None);

        calls.Should().Be(0);
        harness.Bus.ActiveAdvice().Should().BeEmpty();
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.Path.Should().Be("rules");
        record.EscalationReason.Should().Contain("dominant_unwired_thought=social");
        record.Llm.Should().BeNull();
    }

    [Fact]
    public async Task RulesFirst_WhenRulesEscalate_AwaitsDashboardConfirmation()
    {
        CapturingReplayWriter replay = new();
        int calls = 0;
        Harness harness = new(replay, (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(new WelfareLlmResponse([WelfareAdvice("llm_welfare")], []));
        });

        harness.SetSocialPressureState();
        await harness.Minister.RunPlayCycle(PlayCycleContext.CabinetRefresh, CancellationToken.None);

        calls.Should().Be(0);
        harness.Bus.ActiveAdvice().Should().BeEmpty();
        MinisterReplayRecord record = replay.Records.Should().ContainSingle().Subject;
        record.Path.Should().Be("rules");
        record.EscalationReason.Should().Contain("dominant_unwired_thought=social");
        record.Llm.Should().BeNull();
    }

    [Fact]
    public async Task ManualForceLlmFailure_KeepsPriorSnapshotAndPersistsFailedReplay()
    {
        CapturingReplayWriter replay = new();
        Harness harness = new(replay, (_, _, _, _) => throw new InvalidOperationException("quota exhausted"));
        harness.SetNewColonyState();
        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualRulesOnly, CancellationToken.None);
        harness.Bus.ActiveAdvice().Should().ContainSingle()
            .Which.Id.Should().Be("welfare_shelter_floor");

        await harness.Minister.RunPlayCycle(PlayCycleContext.ManualForceLlm, CancellationToken.None);

        harness.Bus.ActiveAdvice().Should().ContainSingle()
            .Which.Id.Should().Be("welfare_shelter_floor");
        MinisterReplayRecord record = replay.Records.Single(r => r.Path == "llm_failed");
        record.WakeupPayload.Should().Be("dashboard:llm");
        record.EscalationReason.Should().Contain("dashboard Run LLM");
        record.Error.Should().NotBeNull();
        record.Error!.Message.Should().Be("quota exhausted");
    }

    private static AdviceItem WelfareAdvice(string id) => new(
        Id: id,
        Minister: "Welfare",
        Priority: Priority.Medium,
        Title: "Check social tension",
        Body: "Alice has social mood pressure.",
        Rationale: "Welfare LLM selected a specific pawn intervention.",
        Actions: [new AdviceAction(AdviceActionKind.Note, "Check the social conflict before it worsens.")],
        GuideCitationIds: [],
        Stamp: new AdviceStamp(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(4)));

    private sealed class Harness
    {
        public ColonyState Colony { get; } = new();
        public MinisterOutputStore OutputStore { get; } = new();
        public AdviceBus Bus { get; }
        public FlagChannel Flags { get; } = new();
        public BriefingCache Cache { get; }
        public MinisterOfWelfare Minister { get; }

        public Harness(
            IReplayCorpusWriter? replay = null,
            LlmClient.WelfareCallExecutor? executor = null)
        {
            Bus = new AdviceBus(OutputStore);
            Cache = new BriefingCache(Colony, new TestLogger<BriefingCache>());
            LlmClient llm = new(
                NullLogger<LlmClient>.Instance,
                executor ?? ((_, _, _, _) => Task.FromResult(new WelfareLlmResponse([], []))));
            WelfareRagRetriever retriever = new(
                new KnowledgeBase(),
                null,
                enabled: false,
                topK: 0,
                NullLogger<WelfareRagRetriever>.Instance);
            Minister = new(
                Cache,
                new Rules(new FixedTimeProvider(FixedNow)),
                OutputStore,
                Bus,
                Flags,
                llm,
                retriever,
                NullLogger<MinisterOfWelfare>.Instance,
                replay is null ? null : new MinisterReplayRecorder(replay));
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

        public void SetSocialPressureState()
        {
            Colony.LastRefreshSource = ColonyStateOrigin.Live;
            Colony.LastLiveRefreshAt = FixedNow;
            Colony.Economy.Update(new EconomyLedger(180_000, 0f, "Cassandra", "Playing", false, ""));
            Colony.Colonists.Update(new ColonistRegistry(
            [
                Colonist("p1", "Alice", 0.46f) with
                {
                    MoodThoughts =
                    [
                        new MoodThoughtRecord("SocialFight", "social fight", -5f, 0)
                    ]
                },
                Colonist("p2", "Bob", 0.66f),
                Colonist("p3", "Cora", 0.72f)
            ]));
            Colony.Rooms.Update(new RoomRegistry(
            [
                new RoomRecord(
                    Id: "bedroom-1",
                    RoleLabel: "Bedroom",
                    Temperature: 21f,
                    CellsCount: 18,
                    TouchesMapEdge: false,
                    IsPrisonCell: false,
                    IsDoorway: false,
                    OpenRoofCount: 0,
                    ContainedBedIds: ["b1", "b2", "b3"],
                    Impressiveness: 30f,
                    Beauty: 1f,
                    Cleanliness: 0f,
                    Space: 18f,
                    Wealth: 500f)
            ]));
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
