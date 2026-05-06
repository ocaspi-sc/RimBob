using FluentAssertions;
using RimAI.Core.Aggregates;
using RimAI.State;
using RimAI.State.Derivations;

namespace RimAI.Tests.State;

public sealed class MayorBriefingDerivationTests
{
    [Fact]
    public void Compute_MoodBuckets_PartitionPawnsCorrectly()
    {
        // Mood 0.20 (break risk), 0.45 (stressed), 0.80 (content)
        var s = StateWith(new[]
        {
            Pawn("Alice", mood: 0.20f),
            Pawn("Bob",   mood: 0.45f),
            Pawn("Carol", mood: 0.80f),
        });

        var b = MayorBriefingDerivation.Compute(s);

        b.Mood.BreakRiskCount.Should().Be(1);
        b.Mood.StressedCount.Should().Be(1);
        b.Mood.ContentCount.Should().Be(1);
        b.Mood.AverageMood.Should().BeApproximately((0.20f + 0.45f + 0.80f) / 3f, 0.001f);
    }

    [Fact]
    public void Compute_NoLords_NoActiveRaid()
    {
        var s = StateWith([]);

        var b = MayorBriefingDerivation.Compute(s);

        b.Threat.ActiveRaid.Should().BeFalse();
        b.Threat.HostileLordCount.Should().Be(0);
        b.Threat.ActiveRaids.Should().BeEmpty();
    }

    [Fact]
    public void Compute_RaidLord_ActivatesRaidWithThreatPoints()
    {
        var s = StateWith([]);
        s.Threats.Update(new ThreatBoard(
            [new HostileLord("l1", "Raid", "Pirates", ThreatPoints: 350f, PawnCount: 8)],
            []));

        var b = MayorBriefingDerivation.Compute(s);

        b.Threat.ActiveRaid.Should().BeTrue();
        b.Threat.HostileLordCount.Should().Be(1);
        b.Threat.TotalThreatPoints.Should().Be(350f);
        b.Threat.ActiveRaids.Should().ContainSingle()
            .Which.PawnCount.Should().Be(8);
    }

    [Fact]
    public void Compute_PowerNet_IsProductionMinusConsumption()
    {
        var s = StateWith([]);
        s.Power.Update(new PowerNetwork(2000f, 1500f, 100f, 500f));

        var b = MayorBriefingDerivation.Compute(s);

        b.Power.NetW.Should().Be(500f);
        b.Power.ProductionW.Should().Be(2000f);
        b.Power.ConsumptionW.Should().Be(1500f);
    }

    [Fact]
    public void Compute_Wealth_DividesByColonistCount()
    {
        var s = StateWith(Enumerable.Range(0, 5).Select(i => Pawn($"P{i}", mood: 0.5f)).ToArray());
        s.Economy.Update(new EconomyLedger(
            Tick: 1000, ColonyWealth: 10_000f, Storyteller: "C", ProgramState: "Playing",
            Paused: false, DateTimeRaw: ""));

        var b = MayorBriefingDerivation.Compute(s);

        b.Wealth.Colony.Should().Be(10_000f);
        b.Wealth.ColonistCount.Should().Be(5);
        b.Wealth.WealthPerColonist.Should().Be(2_000f);
    }

    [Fact]
    public void Compute_NoStockpileItems_LeavesResourcesEmpty()
    {
        var s = StateWith([]);
        // Default stockpile is already empty.

        var b = MayorBriefingDerivation.Compute(s);

        b.Resources.Materials.Should().BeEmpty();
        b.Resources.Medicine.Should().BeEmpty();
        b.Resources.Weapons.Should().BeEmpty();
    }

    [Fact]
    public void Compute_StockpileWithItems_GroupsByCategory()
    {
        var s = StateWith([]);
        s.Stockpiles.Update(new StockpileLedger(
            Zones: [],
            ItemsByDef: new Dictionary<string, int>
            {
                ["Steel"]              = 1500,
                ["WoodLog"]            = 300,
                ["MedicineHerbal"]     = 25,
                ["MedicineIndustrial"] = 12,
                ["Gun_Revolver"]       = 3,
                ["MeleeWeapon_Knife"]  = 2,
                ["Pemmican"]           = 200,
            }));

        var b = MayorBriefingDerivation.Compute(s);

        b.Resources.Materials.Should().ContainKey("Steel").WhoseValue.Should().Be(1500);
        b.Resources.Materials.Should().ContainKey("WoodLog");
        b.Resources.Medicine.Should().HaveCount(2);
        b.Resources.Weapons.Should().HaveCount(2);
        b.Food.EstimatedFoodUnitsInStockpile.Should().Be(200);
    }

    [Fact]
    public void Compute_ColonistCap_TruncatesAndReportsRemaining()
    {
        var s = StateWith(Enumerable.Range(0, 15).Select(i => Pawn($"P{i}", mood: 0.5f)).ToArray());

        var b = MayorBriefingDerivation.Compute(s);

        b.Colonists.Count.Should().Be(15);
        b.Colonists.Pawns.Should().HaveCount(12);
        b.Colonists.AdditionalNotShown.Should().Be(3);
    }

    [Fact]
    public void Compute_ColonistsUnderCap_NoAdditional()
    {
        var s = StateWith(Enumerable.Range(0, 5).Select(i => Pawn($"P{i}", mood: 0.5f)).ToArray());

        var b = MayorBriefingDerivation.Compute(s);

        b.Colonists.AdditionalNotShown.Should().BeNull();
    }

    [Fact]
    public void Compute_TopSkill_PrefersHighestLevelWithPassionGlyph()
    {
        var s = StateWith(
        [
            Pawn("Alice", mood: 0.5f, skills:
            [
                new ColonistSkill("Cooking", 8, "None"),
                new ColonistSkill("Plants", 14, "Major"),
                new ColonistSkill("Construction", 12, "Minor"),
            ])
        ]);

        var b = MayorBriefingDerivation.Compute(s);

        b.Colonists.Pawns.Single().TopSkill.Should().Be("Plants 14**");
    }

    [Fact]
    public void Compute_SkillCoverage_AggregatesAcrossPawns()
    {
        var s = StateWith(
        [
            Pawn("A", mood: 0.5f, skills:
            [
                new ColonistSkill("Plants",  10, "Major"),
                new ColonistSkill("Medicine", 4, "None"),
            ]),
            Pawn("B", mood: 0.5f, skills:
            [
                new ColonistSkill("Plants",  6,  "Minor"),
                new ColonistSkill("Medicine", 8, "Major"),
            ]),
        ]);

        var b = MayorBriefingDerivation.Compute(s);

        b.Skills.ByDef["Plants"].BestLevel.Should().Be(10);
        b.Skills.ByDef["Plants"].PassionatePawns.Should().Be(2);
        b.Skills.ByDef["Plants"].QualifiedPawns.Should().Be(2);
        b.Skills.ByDef["Medicine"].BestLevel.Should().Be(8);
        b.Skills.ByDef["Medicine"].PassionatePawns.Should().Be(1);
        b.Skills.ByDef["Medicine"].QualifiedPawns.Should().Be(1);
        b.Skills.ByDef["Cooking"].Should().BeEquivalentTo(new { BestLevel = 0, PassionatePawns = 0, QualifiedPawns = 0 });
    }

    [Fact]
    public void Compute_TraitCallouts_PartitionRiskAndStrength()
    {
        var s = StateWith(
        [
            Pawn("A", mood: 0.5f, traits: ["Pyromaniac", "Industrious"]),
            Pawn("B", mood: 0.5f, traits: ["Tough", "Nimble"]),
        ]);

        var b = MayorBriefingDerivation.Compute(s);

        b.Traits.Risks.Should().Contain("Pyromaniac");
        b.Traits.Strengths.Should().Contain(["Industrious", "Tough", "Nimble"]);
    }

    [Fact]
    public void Compute_SeasonContext_DerivesDaysToWinter_Aprimay()
    {
        var s = StateWith([]);
        s.Economy.Update(new EconomyLedger(0, 0, "", "", false, "5th of Aprimay, 5500, 14h"));

        var b = MayorBriefingDerivation.Compute(s);

        b.Season.CurrentSeason.Should().Be("Aprimay");
        // Aprimay → Jugust → Septober → Decembary. From day 5 of Aprimay:
        // days to next quadrum = 11; full Jugust (15) + full Septober (15) + 11 = 41.
        b.Season.DaysToNextSeason.Should().Be(11);
        b.Season.DaysToWinter.Should().Be(41);
    }

    [Fact]
    public void Compute_SeasonContext_DaysToWinterZeroInDecembary()
    {
        var s = StateWith([]);
        s.Economy.Update(new EconomyLedger(0, 0, "", "", false, "1st of Decembary, 5500, 0h"));

        var b = MayorBriefingDerivation.Compute(s);

        b.Season.CurrentSeason.Should().Be("Decembary");
        b.Season.DaysToWinter.Should().Be(0);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ColonyState StateWith(IReadOnlyList<ColonistRecord> colonists)
    {
        var s = new ColonyState();
        s.Colonists.Update(new ColonistRegistry(colonists));
        return s;
    }

    private static ColonistRecord Pawn(
        string name,
        float mood = 0.5f,
        float health = 1.0f,
        IReadOnlyList<ColonistSkill>? skills = null,
        IReadOnlyList<string>? traits = null) =>
        new(
            Id: name, Name: name, Age: 30, Gender: "Male",
            Health: health, Mood: mood, Hunger: 1.0f,
            CurrentJob: null,
            Skills: skills ?? [],
            Traits: traits ?? []
        );
}
