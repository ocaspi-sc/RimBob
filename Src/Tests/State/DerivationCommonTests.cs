using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.Core.Briefings;
using RimAI.State.Derivations.Common;

namespace RimAI.Tests.State;

public sealed class DerivationCommonTests
{
    [Fact]
    public void SeasonDeriver_ComputesDaysToWinterForAprimay()
    {
        SeasonContext season = SeasonDeriver.Derive(new DateStamp("5th of Aprimay", 5500, "Aprimay", 5, 14));

        season.CurrentSeason.Should().Be("Aprimay");
        season.DaysToNextSeason.Should().Be(11);
        season.DaysToWinter.Should().Be(41);
    }

    [Fact]
    public void PawnDeriver_FiltersDeadColonistsAndAggregatesSkills()
    {
        IReadOnlyList<ColonistRecord> pawns =
        [
            Pawn("alive", false, [new ColonistSkill("Cooking", 8, "Major")]),
            Pawn("dead", true, [new ColonistSkill("Cooking", 14, "Major")])
        ];

        IReadOnlyList<ColonistRecord> living = PawnDeriver.LivingColonists(pawns);

        living.Should().ContainSingle().Which.Id.Should().Be("alive");
        PawnDeriver.BestSkill(living, "Cooking").Should().Be(8);
        PawnDeriver.QualifiedSkillCount(living, "Cooking", 6).Should().Be(1);
    }

    [Fact]
    public void ThreatDeriver_RecognizesHostileLordJobTypes()
    {
        ThreatBoard board = new(
            [
                new HostileLord("raid", "RaidEnemy", "Pirates", 350f, 4),
                new HostileLord("travel", "Traveling", "Traders", null, 3)
            ],
            []);

        ThreatDeriver.HasActiveHostileThreat(board).Should().BeTrue();
        ThreatDeriver.HostileLords(board).Should().ContainSingle().Which.Id.Should().Be("raid");
    }

    [Fact]
    public void BuildingClassifier_SeparatesFoodAndStrategicBuildings()
    {
        BuildingClassifier.IsCookingBuilding(Building("FueledStove")).Should().BeTrue();
        BuildingClassifier.IsButcherTable(Building("TableButcher")).Should().BeTrue();
        BuildingClassifier.IsGenerator(Building("SolarPanel")).Should().BeTrue();
        BuildingClassifier.IsBed(Building("HospitalBed")).Should().BeFalse();
    }

    [Fact]
    public void MapDistance_ComputesNearestManhattanDistanceAndProximity()
    {
        IReadOnlyList<MapPosition> from = [new(0, 0, 0), new(20, 0, 20)];
        IReadOnlyList<MapPosition> to = [new(4, 0, 3)];

        int? distance = MapDistance.Nearest(from, to);

        distance.Should().Be(7);
        MapDistance.ProximityLabel(distance, "kitchen").Should().Be("adjacent (7 cells from kitchen)");
    }

    private static ColonistRecord Pawn(string id, bool isDead, IReadOnlyList<ColonistSkill> skills) =>
        new(
            Id: id,
            Name: id,
            Age: 30,
            Gender: "Female",
            Health: 1f,
            Mood: 0.7f,
            Hunger: 1f,
            IsDowned: false,
            IsDead: isDead,
            Position: null,
            CurrentJob: null,
            Skills: skills,
            Traits: []);

    private static BuildingRecord Building(string def) =>
        new(def, def, 1f, null, null);
}
