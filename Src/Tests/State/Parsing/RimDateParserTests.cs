using FluentAssertions;
using RimBob.Core.Briefings;
using RimBob.State.Parsing;

namespace RimBob.Tests.State.Parsing;

public sealed class RimDateParserTests
{
    [Fact]
    public void Parse_CanonicalString_FillsAllFields()
    {
        GameDate d = RimDateParser.Parse("5th of Aprimay, 5500, 14h", 300_000);

        d.RawRimWorldDate.Should().Be("5th of Aprimay, 5500, 14h");
        d.QuadrumDay.Should().Be(5);
        d.Quadrum.Should().Be("Aprimay");
        d.RimWorldYear.Should().Be(5500);
        d.Hour.Should().Be(14);
        d.TotalDays.Should().Be(5.0);
        d.CompletedDays.Should().Be(5);
        d.ColonyDay.Should().Be(6);
        d.ColonyYear.Should().Be(1);
        d.DayOfYear.Should().Be(6);
        d.Label.Should().Be("Y1 D6, Aprimay 5, 14h");
    }

    [Theory]
    [InlineData("1st of Aprimay, 5500, 0h",   1, "Aprimay",  5500, 0)]
    [InlineData("23rd of Decembary, 5499, 6h",23, "Decembary", 5499, 6)]
    [InlineData("2nd of Jugust, 5501, 23h",   2, "Jugust",   5501, 23)]
    public void Parse_HandlesOrdinalSuffixes(string raw, int day, string q, int year, int hour)
    {
        GameDate d = RimDateParser.Parse(raw, 0);
        d.QuadrumDay.Should().Be(day);
        d.Quadrum.Should().Be(q);
        d.RimWorldYear.Should().Be(year);
        d.Hour.Should().Be(hour);
        d.ColonyDay.Should().Be(1);
    }

    [Fact]
    public void Parse_Garbage_PreservesRawWithNullStructured()
    {
        GameDate d = RimDateParser.Parse("garbage", 60_000);
        d.RawRimWorldDate.Should().Be("garbage");
        d.QuadrumDay.Should().BeNull();
        d.Quadrum.Should().BeNull();
        d.RimWorldYear.Should().BeNull();
        d.Hour.Should().BeNull();
        d.ColonyDay.Should().Be(2);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptyRaw()
    {
        GameDate d = RimDateParser.Parse("", 0);
        d.RawRimWorldDate.Should().Be("");
        d.QuadrumDay.Should().BeNull();
        d.Label.Should().Be("Y1 D1");
    }
}
