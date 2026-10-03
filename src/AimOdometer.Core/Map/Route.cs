namespace AimOdometer.Core.Map;

/// <summary>A city or address picked by the user. <paramref name="Name"/> is short ("Odesa"), the display name full.</summary>
public sealed record Place(string Name, string DisplayName, GeoPoint Point);

/// <summary>
/// One leg of the journey. <paramref name="Meters"/> is the road distance (or the great-circle distance when
/// <paramref name="IsStraight"/>, e.g. across an ocean); <paramref name="Line"/> is the shape drawn on the map.
/// </summary>
public sealed record RouteLeg(Place From, Place To, double Meters, IReadOnlyList<GeoPoint> Line, bool IsStraight);

/// <summary>
/// Where a distance takes you along the legs. <paramref name="Finished"/> when it reaches the last place;
/// <paramref name="ExtraMeters"/> is what is left over beyond it.
/// </summary>
public sealed record RoutePosition(int LegIndex, GeoPoint Point, double Fraction, bool Finished, double ExtraMeters);

/// <summary>A chain of legs: start → first destination → next destination → …</summary>
public static class RoutePlan
{
    public static double TotalMeters(IReadOnlyList<RouteLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        return legs.Sum(l => l.Meters);
    }

    /// <summary>The position after walking <paramref name="meters"/> from the start of the first leg.</summary>
    public static RoutePosition? Locate(IReadOnlyList<RouteLeg> legs, double meters)
    {
        ArgumentNullException.ThrowIfNull(legs);
        if (legs.Count == 0)
        {
            return null;
        }

        var remaining = Math.Max(0, meters);
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            if (remaining < leg.Meters)
            {
                var fraction = leg.Meters <= 0 ? 1 : remaining / leg.Meters;
                return new RoutePosition(i, Geo.PointAlong(leg.Line, fraction), fraction, false, 0);
            }

            remaining -= leg.Meters;
        }

        return new RoutePosition(legs.Count - 1, legs[^1].Line[^1], 1, true, remaining);
    }

    /// <summary>The walked part of the journey as one line, for highlighting on the map.</summary>
    public static IReadOnlyList<GeoPoint> Walked(IReadOnlyList<RouteLeg> legs, double meters)
    {
        ArgumentNullException.ThrowIfNull(legs);
        var position = Locate(legs, meters);
        if (position is null)
        {
            return [];
        }

        var line = new List<GeoPoint>();
        for (var i = 0; i < position.LegIndex; i++)
        {
            line.AddRange(legs[i].Line);
        }

        line.AddRange(Geo.Cut(legs[position.LegIndex].Line, position.Fraction));
        return line;
    }
}
