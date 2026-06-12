using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class UnitsTests
{
    [Fact]
    public void Length_RoundTripsAcrossUnits()
    {
        var len = Length.FromFeet(10);
        Assert.Equal(3.048, len.Meters, 6);
        Assert.Equal(120, len.Inches, 6);
        Assert.Equal(304.8, len.Centimeters, 6);
    }

    [Fact]
    public void Length_TimesLength_GivesArea()
    {
        var area = Length.FromMeters(6) * Length.FromMeters(8);
        Assert.Equal(48, area.SquareMeters, 6);
    }

    [Fact]
    public void Area_ConvertsToSquareFeet()
    {
        var area = Area.FromSquareMeters(48);
        Assert.Equal(516.668, area.SquareFeet, 2);
    }

    [Fact]
    public void Area_ClampNonNegative_FloorsAtZero()
    {
        var area = Area.FromSquareMeters(2) - Area.FromSquareMeters(5);
        Assert.True(area.SquareMeters < 0);
        Assert.Equal(0, area.ClampNonNegative().SquareMeters);
    }

    [Fact]
    public void Formatter_Metric_RendersMeters()
    {
        Assert.Equal("2.5 m", UnitFormatter.Instance.FormatLength(Length.FromMeters(2.5), UnitSystem.Metric));
        Assert.Equal("48 m²", UnitFormatter.Instance.FormatArea(Area.FromSquareMeters(48), UnitSystem.Metric));
    }

    [Fact]
    public void Formatter_Imperial_RendersFeetInches()
    {
        // 2.5 ft → 2 ft 6 in
        Assert.Equal("2 ft 6 in", UnitFormatter.Instance.FormatLength(Length.FromFeet(2.5), UnitSystem.Imperial));
    }

    [Fact]
    public void Formatter_Imperial_CarriesInchesIntoFeet()
    {
        // 11.99 in rounds to 12.0 → should carry to 1 ft 0 in.
        Assert.Equal("1 ft 0 in", UnitFormatter.Instance.FormatLength(Length.FromInches(11.99), UnitSystem.Imperial));
    }

    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("250 cm", 2.5)]
    [InlineData("2.5 m", 2.5)]
    public void Parser_Metric(string input, double expectedMeters)
    {
        Assert.True(UnitConverter.TryParse(input, UnitSystem.Metric, out var len));
        Assert.Equal(expectedMeters, len.Meters, 6);
    }

    [Theory]
    [InlineData("10", 3.048)]          // bare number → feet
    [InlineData("8 ft 2 in", 2.4892)]
    [InlineData("8' 2\"", 2.4892)]
    [InlineData("8'2", 2.4892)]
    [InlineData("98 in", 2.4892)]
    public void Parser_Imperial(string input, double expectedMeters)
    {
        Assert.True(UnitConverter.TryParse(input, UnitSystem.Imperial, out var len));
        Assert.Equal(expectedMeters, len.Meters, 4);
    }

    [Fact]
    public void Parser_RejectsGarbage()
    {
        Assert.False(UnitConverter.TryParse("abc", UnitSystem.Metric, out _));
        Assert.False(UnitConverter.TryParse("", UnitSystem.Imperial, out _));
    }
}
