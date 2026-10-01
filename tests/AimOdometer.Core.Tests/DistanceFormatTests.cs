using System.Globalization;

namespace AimOdometer.Core.Tests;

public class DistanceFormatTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "0 cm")]
    [InlineData(42.4, "42 cm")]
    [InlineData(150, "1.5 m")]
    [InlineData(85_000, "850 m")]
    [InlineData(120_000, "1.20 km")]
    [InlineData(4_219_500, "42.20 km")]
    [InlineData(123_456_789, "1,235 km")]
    public void Metric(double cm, string expected)
    {
        Assert.Equal(expected, Normalize(DistanceFormat.Format(cm, UnitSystem.Metric, UnitLabels.English, Invariant)));
    }

    [Theory]
    [InlineData(25.4, "10 in")]
    [InlineData(304.8, "10 ft")]
    [InlineData(160_934.4, "1.00 mi")]
    public void Imperial(double cm, string expected)
    {
        Assert.Equal(expected, Normalize(DistanceFormat.Format(cm, UnitSystem.Imperial, UnitLabels.English, Invariant)));
    }

    [Fact]
    public void NegativeIsClampedToZero()
    {
        Assert.Equal("0 cm", Normalize(DistanceFormat.Format(-5, UnitSystem.Metric, UnitLabels.English, Invariant)));
    }

    [Fact]
    public void UsesCultureDecimalSeparator()
    {
        var russian = CultureInfo.GetCultureInfo("ru-RU");
        Assert.Equal("1,5 m", Normalize(DistanceFormat.Format(150, UnitSystem.Metric, UnitLabels.English, russian)));
    }

    [Theory]
    [InlineData("imperial", UnitSystem.Imperial)]
    [InlineData("IMPERIAL", UnitSystem.Imperial)]
    [InlineData("metric", UnitSystem.Metric)]
    [InlineData(null, UnitSystem.Metric)]
    [InlineData("garbage", UnitSystem.Metric)]
    public void ParseUnits(string? value, UnitSystem expected) => Assert.Equal(expected, DistanceFormat.ParseUnits(value));

    // Number and unit are joined by a non-breaking space; compare with a normal one for readability.
    private static string Normalize(string text) => text.Replace(' ', ' ');
}
