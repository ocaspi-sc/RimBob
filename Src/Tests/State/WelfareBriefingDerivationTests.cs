using FluentAssertions;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.State;
using RimBob.State.Derivations;

namespace RimBob.Tests.State;

public sealed class WelfareBriefingDerivationTests
{
    [Fact]
    public void Compute_RanksLowMoodLowNeedsAndWorstRooms()
    {
        ColonyState state = new();
        state.Economy.Update(new EconomyLedger(42, 0f, "Cassandra", "Playing", false, ""));
        state.Colonists.Update(new ColonistRegistry(
        [
            new ColonistRecord(
                Id: "p1",
                Name: "Alice",
                Age: 28,
                Gender: "Female",
                Health: 1f,
                Mood: 0.28f,
                Hunger: 0.8f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [],
                Traits: [],
                Sleep: 0.22f,
                Comfort: 0.2f,
                Beauty: 0.15f,
                Joy: 0.31f,
                FreshAir: 0.7f,
                DrugsDesire: 0f,
                MoodThoughts:
                [
                    new MoodThoughtRecord("AteWithoutTable", "ate without table", -3f, 0),
                    new MoodThoughtRecord("SleptInCold", "slept in the cold", -4f, 1),
                    new MoodThoughtRecord("AteFineMeal", "ate fine meal", 5f, 0)
                ]),
            new ColonistRecord(
                Id: "p2",
                Name: "Bob",
                Age: 32,
                Gender: "Male",
                Health: 1f,
                Mood: 0.72f,
                Hunger: 0.9f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [],
                Traits: [],
                Sleep: 0.8f,
                Comfort: 0.8f,
                Beauty: 0.8f,
                Joy: 0.8f,
                FreshAir: 0.8f,
                DrugsDesire: 0f)
        ]));
        state.Rooms.Update(new RoomRegistry(
        [
            new RoomRecord(
                Id: "room-good",
                RoleLabel: "bedroom",
                Temperature: 21f,
                CellsCount: 20,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 0,
                ContainedBedIds: ["bed-1"],
                Impressiveness: 42f,
                Beauty: 3f,
                Cleanliness: 0.5f,
                Space: 20f,
                Wealth: 600f),
            new RoomRecord(
                Id: "room-bad",
                RoleLabel: "barracks",
                Temperature: 10f,
                CellsCount: 8,
                TouchesMapEdge: false,
                IsPrisonCell: false,
                IsDoorway: false,
                OpenRoofCount: 2,
                ContainedBedIds: ["bed-2"],
                Impressiveness: 12f,
                Beauty: -3f,
                Cleanliness: -1f,
                Space: 8f,
                Wealth: 120f)
        ]));

        WelfareSourceBriefing briefing = WelfareBriefingDerivation.Compute(state, 7);

        briefing.BriefingVersion.Should().Be(7);
        briefing.GameTick.Should().Be(42);
        briefing.Mood.AverageMood.Should().BeApproximately(0.5f, 0.0001f);
        briefing.Mood.BreakRiskCount.Should().Be(1);
        briefing.Mood.ContentCount.Should().Be(1);
        briefing.WorstPawns.Should().HaveCount(2);
        briefing.WorstPawns[0].Name.Should().Be("Alice");
        briefing.WorstPawns[0].TopNegativeThoughts.Select(thought => thought.DefName)
            .Should().Equal("SleptInCold", "AteWithoutTable");
        briefing.NeedLows.Select(low => low.Need)
            .Should().Contain(["beauty", "comfort", "sleep", "joy"]);
        briefing.Rooms.Count.Should().Be(2);
        briefing.Rooms.BedroomCount.Should().Be(2);
        briefing.Rooms.AverageImpressiveness.Should().Be(27f);
        briefing.Rooms.WorstRooms[0].Id.Should().Be("room-bad");
        briefing.DataCoverage.HasNeedLevels.Should().BeTrue();
        briefing.DataCoverage.HasMoodThoughts.Should().BeTrue();
        briefing.DataCoverage.HasRooms.Should().BeTrue();
        briefing.DataCoverage.HasRoomQuality.Should().BeTrue();
    }

    [Fact]
    public void Compute_WhenRoomsRefreshReturnsEmptyList_MarksRoomCoveragePresent()
    {
        ColonyState state = new();
        state.Economy.Update(new EconomyLedger(42, 0f, "Cassandra", "Playing", false, ""));
        state.Colonists.Update(new ColonistRegistry(
        [
            new ColonistRecord(
                Id: "p1",
                Name: "Alice",
                Age: 28,
                Gender: "Female",
                Health: 1f,
                Mood: 0.72f,
                Hunger: 0.8f,
                IsDowned: false,
                IsDead: false,
                Position: null,
                CurrentJob: null,
                Skills: [],
                Traits: [],
                Sleep: 0.8f,
                Comfort: 0.8f,
                Beauty: 0.8f,
                Joy: 0.8f,
                FreshAir: 0.8f,
                DrugsDesire: 0f)
        ]));
        state.Rooms.Update(new RoomRegistry([]));

        WelfareSourceBriefing briefing = WelfareBriefingDerivation.Compute(state, 8);

        briefing.Rooms.Count.Should().Be(0);
        briefing.Rooms.BedroomCount.Should().Be(0);
        briefing.DataCoverage.HasRooms.Should().BeTrue();
    }
}
