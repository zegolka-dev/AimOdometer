using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Fun;

/// <summary>How worn a piece of gear is.</summary>
public sealed record GearStatus(GearItem Item, double KilometersUsed)
{
    /// <summary>Above this share of the expected lifetime the UI says it is time to replace it.</summary>
    public const double ReplaceThreshold = 0.9;

    public double Fraction => Item.LifetimeKm <= 0 ? 0 : KilometersUsed / Item.LifetimeKm;

    public bool TimeToReplace => Item.RetiredOn is null && Fraction >= ReplaceThreshold;
}

/// <summary>Wear of mouse pads, mice, glides and arm sleeves: distance since they were put into use.</summary>
public static class GearWear
{
    /// <summary>Editable defaults: a cloth pad, a mouse, a set of PTFE glides, a gaming arm sleeve.</summary>
    public static double DefaultLifetimeKm(GearKind kind) => kind switch
    {
        GearKind.MousePad => 1000,
        GearKind.Mouse => 3000,
        GearKind.Sleeve => 1500,
        _ => 250,
    };

    /// <summary>
    /// A pad or a sleeve wears with every mouse (all counted devices); a mouse or its glides only with that mouse.
    /// Retired items stop counting on their retirement date.
    /// </summary>
    public static GearStatus Status(StatsStore store, GearItem item)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(item);
        var deviceId = UsesAllMice(item.Kind) ? null : item.DeviceId;
        var used = store.CentimetersSince(item.StartedOn, deviceId);
        if (item.RetiredOn is { } retired)
        {
            used -= store.CentimetersSince(retired.AddDays(1), deviceId);
        }

        return new GearStatus(item, Math.Max(0, used) / 100_000);
    }

    /// <summary>True for gear that is not tied to one mouse.</summary>
    public static bool UsesAllMice(GearKind kind) => kind is GearKind.MousePad or GearKind.Sleeve;
}
