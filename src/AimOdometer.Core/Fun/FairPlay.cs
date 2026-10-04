namespace AimOdometer.Core.Fun;

/// <summary>
/// Limits that tell real movement from an inflated one. Distance is counts ÷ DPI, so telling AimOdometer a DPI far
/// below the mouse's real one multiplies every kilometre. Crossing a limit even once unlocks a "shame" achievement
/// that also shows next to the name on leaderboards. The same numbers are in supabase/migrations/*_fair_play.sql and
/// supabase/functions/_shared/validate.ts; keep them in sync.
/// </summary>
public static class FairPlay
{
    /// <summary>Clown: a day of at least <see cref="ClownDayMeters"/> at a DPI below this.</summary>
    public const double ClownMaxDpi = 200;
    public const double ClownDayMeters = 10_000;

    /// <summary>Blockhead: at least <see cref="BlockheadMeters"/> moved with a DPI below this (no mouse goes that low).</summary>
    public const double BlockheadMaxDpi = 100;
    public const double BlockheadMeters = 100;

    /// <summary>Fool: a flick no hand can make (the fastest human flicks are about 10 m/s).</summary>
    public const double FoolFlickMetersPerSecond = 30;

    /// <summary>Booster: a day or a flick the cloud rejects as impossible.</summary>
    public const double BoostDayMeters = 100_000;
    public const double BoostFlickMetersPerSecond = 50;

    /// <summary>Distance-weighted DPI: what a stretch of movement was measured with.</summary>
    public static double WeightedDpi(IEnumerable<(double Centimeters, double Dpi)> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        double weighted = 0, total = 0;
        foreach (var (cm, dpi) in parts)
        {
            if (cm > 0 && dpi > 0)
            {
                weighted += cm * dpi;
                total += cm;
            }
        }

        return total > 0 ? weighted / total : 0;
    }
}
