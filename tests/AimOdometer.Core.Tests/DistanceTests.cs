using AimOdometer.Core;

namespace AimOdometer.Core.Tests;

public class DistanceTests
{
    [Fact]
    public void OneInchOfCounts_IsTwoPointFiveFourCentimeters()
    {
        Assert.Equal(2.54, Distance.CountsToCentimeters(800, 800), precision: 10);
    }

    [Theory]
    [InlineData(1600, 400, 10.16)]
    [InlineData(3150, 1600, 5.000625)]
    [InlineData(0, 800, 0)]
    public void CountsToCentimeters_UsesDpi(double counts, double dpi, double expectedCm)
    {
        Assert.Equal(expectedCm, Distance.CountsToCentimeters(counts, dpi), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-400)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CountsToCentimeters_RejectsInvalidDpi(double dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Distance.CountsToCentimeters(100, dpi));
    }

    [Theory]
    [InlineData(3, 4, 5)]
    [InlineData(-3, -4, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    public void PathCounts_IsEuclideanLength(int dx, int dy, double expected)
    {
        Assert.Equal(expected, Distance.PathCounts(dx, dy), precision: 6);
    }
}
