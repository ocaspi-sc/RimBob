using FluentAssertions;
using RimBob.State.Parsing;

namespace RimBob.Tests.State.Parsing;

public sealed class RimDateParserTests
{
    [Fact]
    public void Parse_CanonicalString_FillsAllFields()
    {
        var d = RimDateParser.Parse("5th of Aprimay, 5500, 14h");

        d.Raw.Should().Be("5th of Aprimay, 5500, 14h");
        d.Day.Should().Be(5);
        d.Quadrum.Should().Be("Aprimay");
        d.Year.Should().Be(5500);
        d.Hour.Should().Be(14);
    }

    [Theory]
    [InlineData("1st of Aprimay, 5500, 0h",   1, "Aprimay",  5500, 0)]
    [InlineData("23rd of Decembary, 5499, 6h",23, "Decembary", 5499, 6)]
    [InlineData("2nd of Jugust, 5501, 23h",   2, "Jugust",   5501, 23)]
    public void Parse_HandlesOrdinalSuffixes(string raw, int day, string q, int year, int hour)
    {
        var d = RimDateParser.Parse(raw);
        d.Day.Should().Be(day);
        d.Quadrum.Should().Be(q);
        d.Year.Should().Be(year);
        d.Hour.Should().Be(hour);
    }

    [Fact]
    public void Parse_Garbage_PreservesRawWithNullStructured()
    {
        var d = RimDateParser.Parse("garbage");
        d.Raw.Should().Be("garbage");
        d.Day.Should().BeNull();
        d.Quadrum.Should().BeNull();
        d.Year.Should().BeNull();
        d.Hour.Should().BeNull();
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptyRaw()
    {
        var d = RimDateParser.Parse("");
        d.Raw.Should().Be("");
        d.Day.Should().BeNull();
    }
}
