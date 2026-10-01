using System.Globalization;

namespace AimOdometer.Core.Input;

/// <summary>
/// Identifies the local clock hour in which movement happened, together with the UTC offset in effect.
/// The offset disambiguates the repeated hour when DST ends and keeps data correct after time-zone changes.
/// </summary>
public readonly record struct HourKey(DateOnly LocalDate, int LocalHour, int UtcOffsetMinutes)
{
    /// <summary>How often the tracker re-checks the local hour. 15 minutes covers :30 and :45 offsets.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    public static HourKey FromUtc(DateTime utcNow, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Expected a UTC time.", nameof(utcNow));
        }

        var offset = zone.GetUtcOffset(utcNow);
        var local = utcNow + offset;
        return new HourKey(DateOnly.FromDateTime(local), local.Hour, (int)offset.TotalMinutes);
    }

    /// <summary>
    /// The next UTC instant at which the local hour may change: the next quarter-hour boundary in UTC.
    /// Every real-world UTC offset is a multiple of 15 minutes, so local hours always start on such a boundary.
    /// </summary>
    public static DateTime NextCheckUtc(DateTime utcNow)
    {
        var ticks = CheckInterval.Ticks;
        return new DateTime(((utcNow.Ticks / ticks) + 1) * ticks, DateTimeKind.Utc);
    }

    /// <summary>Date formatted as stored in the database (yyyy-MM-dd).</summary>
    public string LocalDateText => LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
