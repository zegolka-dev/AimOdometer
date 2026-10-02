using AimOdometer.App.Localization;
using AimOdometer.Core;

namespace AimOdometer.App.Services;

/// <summary>Numbers as the user sees them: in the chosen units and language.</summary>
public static class Format
{
    private const double CentimetersPerFoot = 30.48;

    public static UnitSystem Units { get; set; } = UnitSystem.Metric;

    private static Loc L => Loc.Instance;

    public static UnitLabels Labels => new(
        L["Unit.Cm"], L["Unit.M"], L["Unit.Km"], L["Unit.In"], L["Unit.Ft"], L["Unit.Mi"]);

    public static string Distance(double centimeters) => DistanceFormat.Format(centimeters, Units, Labels, L.Culture);

    /// <summary>Splits a distance into number and unit, for the big displays where the unit is drawn smaller.</summary>
    public static (string Number, string Unit) DistanceParts(double centimeters)
    {
        var text = Distance(centimeters);
        var space = text.LastIndexOf(' ');
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..]);
    }

    /// <summary>Speed in m/s (metric) or ft/s (imperial).</summary>
    public static string Speed(double centimetersPerSecond) => Units == UnitSystem.Metric
        ? L.Format("Unit.MetersPerSecond", (centimetersPerSecond / 100).ToString("0.0", L.Culture))
        : L.Format("Unit.FeetPerSecond", (centimetersPerSecond / CentimetersPerFoot).ToString("0.0", L.Culture));

    /// <summary>Distance per hour: km/h or mi/h.</summary>
    public static string PerHour(double? kilometersPerHour) => kilometersPerHour is not { } kmh
        ? "—"
        : Units == UnitSystem.Metric
            ? L.Format("Unit.KmPerHour", kmh.ToString("0.00", L.Culture))
            : L.Format("Unit.MiPerHour", (kmh / 1.609344).ToString("0.00", L.Culture));

    public static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? L.Format("Unit.HoursMinutes", (int)span.TotalHours, span.Minutes)
            : span.TotalMinutes >= 1
                ? L.Format("Unit.Minutes", (int)span.TotalMinutes)
                : L.Format("Unit.Seconds", (int)span.TotalSeconds);
    }

    public static string Number(double value, string format = "N0") => value.ToString(format, L.Culture);

    public static string Percent(double fraction) => fraction.ToString("P0", L.Culture);

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", L.Culture);

    public static string ShortDate(DateOnly date) => date.ToString("d MMM", L.Culture);
}
