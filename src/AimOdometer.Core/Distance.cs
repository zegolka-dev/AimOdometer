namespace AimOdometer.Core;

/// <summary>
/// Converts raw mouse sensor counts into physical distance.
/// Counts are what the sensor reports; DPI (counts per inch) maps them to inches.
/// </summary>
public static class Distance
{
    public const double CentimetersPerInch = 2.54;

    /// <summary>Euclidean length of one relative movement report, in counts.</summary>
    public static double PathCounts(int dx, int dy) => Math.Sqrt(((double)dx * dx) + ((double)dy * dy));

    /// <summary>Converts counts to centimeters for a mouse configured with the given DPI.</summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is not a positive finite number.</exception>
    public static double CountsToCentimeters(double counts, double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be a positive finite number.");
        }

        return counts / dpi * CentimetersPerInch;
    }
}
