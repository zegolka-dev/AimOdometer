namespace AimOdometer.Core.Map;

/// <summary>A point on Earth in degrees. Longitude may leave [-180, 180] on lines that cross the antimeridian.</summary>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    /// <summary>The same point with longitude wrapped into [-180, 180).</summary>
    public GeoPoint Normalized => new(Lat, ((((Lon + 180) % 360) + 360) % 360) - 180);
}

/// <summary>Spherical geometry for routes: distances, points along a line, great circles.</summary>
public static class Geo
{
    /// <summary>Mean Earth radius (IUGG).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    private const double ToRadians = Math.PI / 180;

    /// <summary>Great-circle distance (haversine).</summary>
    public static double Distance(GeoPoint a, GeoPoint b)
    {
        var dLat = (b.Lat - a.Lat) * ToRadians;
        var dLon = (b.Lon - a.Lon) * ToRadians;
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(a.Lat * ToRadians) * Math.Cos(b.Lat * ToRadians) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static double Length(IReadOnlyList<GeoPoint> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var total = 0.0;
        for (var i = 1; i < line.Count; i++)
        {
            total += Distance(line[i - 1], line[i]);
        }

        return total;
    }

    /// <summary>
    /// The point at <paramref name="fraction"/> (0..1) of the line's own length. Routing services return a simplified
    /// line that is shorter than the road distance, so positions are proportional rather than absolute.
    /// </summary>
    public static GeoPoint PointAlong(IReadOnlyList<GeoPoint> line, double fraction) =>
        Cut(line, fraction)[^1];

    /// <summary>The line from its start up to <paramref name="fraction"/> of its length (the last point interpolated).</summary>
    public static IReadOnlyList<GeoPoint> Cut(IReadOnlyList<GeoPoint> line, double fraction)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Count == 0)
        {
            throw new ArgumentException("The line is empty.", nameof(line));
        }

        var result = new List<GeoPoint> { line[0] };
        var target = Length(line) * Math.Clamp(fraction, 0, 1);
        var walked = 0.0;
        for (var i = 1; i < line.Count; i++)
        {
            var segment = Distance(line[i - 1], line[i]);
            if (walked + segment >= target)
            {
                var t = segment <= 0 ? 0 : (target - walked) / segment;
                result.Add(Lerp(line[i - 1], line[i], t));
                return result;
            }

            walked += segment;
            result.Add(line[i]);
        }

        return result;
    }

    /// <summary>
    /// Great circle from a to b in <paramref name="segments"/> pieces, with longitudes unwrapped so the line stays
    /// continuous across the antimeridian (MapLibre draws longitudes beyond ±180 on the neighbouring world copy).
    /// </summary>
    public static IReadOnlyList<GeoPoint> GreatCircle(GeoPoint a, GeoPoint b, int segments = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
        var (x1, y1, z1) = ToVector(a);
        var (x2, y2, z2) = ToVector(b);
        var omega = Math.Acos(Math.Clamp((x1 * x2) + (y1 * y2) + (z1 * z2), -1, 1));
        var points = new List<GeoPoint>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var t = (double)i / segments;
            GeoPoint p;
            if (omega < 1e-12)
            {
                p = Lerp(a, b, t);
            }
            else
            {
                var s = Math.Sin(omega);
                var k1 = Math.Sin((1 - t) * omega) / s;
                var k2 = Math.Sin(t * omega) / s;
                p = FromVector((k1 * x1) + (k2 * x2), (k1 * y1) + (k2 * y2), (k1 * z1) + (k2 * z2));
            }

            if (points.Count > 0)
            {
                var previous = points[^1].Lon;
                var lon = p.Lon;
                while (lon - previous > 180)
                {
                    lon -= 360;
                }

                while (previous - lon > 180)
                {
                    lon += 360;
                }

                p = p with { Lon = lon };
            }
            else
            {
                p = a;
            }

            points.Add(p);
        }

        return points;
    }

    private static GeoPoint Lerp(GeoPoint a, GeoPoint b, double t) =>
        new(a.Lat + ((b.Lat - a.Lat) * t), a.Lon + ((b.Lon - a.Lon) * t));

    private static (double X, double Y, double Z) ToVector(GeoPoint p)
    {
        var lat = p.Lat * ToRadians;
        var lon = p.Lon * ToRadians;
        return (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static GeoPoint FromVector(double x, double y, double z) =>
        new(Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) / ToRadians, Math.Atan2(y, x) / ToRadians);
}
