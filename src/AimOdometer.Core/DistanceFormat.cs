using System.Globalization;

namespace AimOdometer.Core;

public enum UnitSystem
{
    Metric = 0,
    Imperial = 1,
}

/// <summary>Localized unit abbreviations, e.g. "cm"/"m"/"km" or "см"/"м"/"км".</summary>
public sealed record UnitLabels(string Centimeter, string Meter, string Kilometer, string Inch, string Foot, string Mile)
{
    public static readonly UnitLabels English = new("cm", "m", "km", "in", "ft", "mi");
}

/// <summary>Human-friendly distance formatting: picks the unit so the number stays readable.</summary>
public static class DistanceFormat
{
    private const double CentimetersPerFoot = 30.48;
    private const double CentimetersPerMile = 160_934.4;

    public static UnitSystem ParseUnits(string? value) =>
        string.Equals(value, "imperial", StringComparison.OrdinalIgnoreCase) ? UnitSystem.Imperial : UnitSystem.Metric;

    public static string Format(double centimeters, UnitSystem units, UnitLabels labels, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(culture);
        var cm = Math.Max(0, centimeters);

        return units == UnitSystem.Metric
            ? cm switch
            {
                < 100 => Join(cm.ToString("0", culture), labels.Centimeter),
                < 100_000 => Join((cm / 100).ToString(cm < 1_000 ? "0.0" : "0", culture), labels.Meter),
                < 10_000_000 => Join((cm / 100_000).ToString("0.00", culture), labels.Kilometer),
                _ => Join((cm / 100_000).ToString("N0", culture), labels.Kilometer),
            }
            : cm switch
            {
                < CentimetersPerFoot => Join((cm / Distance.CentimetersPerInch).ToString("0", culture), labels.Inch),
                < CentimetersPerMile / 10 => Join((cm / CentimetersPerFoot).ToString("0", culture), labels.Foot),
                < CentimetersPerMile * 100 => Join((cm / CentimetersPerMile).ToString("0.00", culture), labels.Mile),
                _ => Join((cm / CentimetersPerMile).ToString("N0", culture), labels.Mile),
            };
    }

    // Non-breaking space keeps the number and unit together.
    private static string Join(string number, string unit) => number + " " + unit;
}
