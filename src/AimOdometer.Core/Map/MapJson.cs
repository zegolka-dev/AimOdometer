using System.Globalization;
using System.Text.Json;

namespace AimOdometer.Core.Map;

/// <summary>An OSRM-compatible routing server: requests go to {Url}/route/v1/{Profile}/{lon,lat;lon,lat}.</summary>
public sealed record RouterService(Uri Url, string Profile);

/// <summary>
/// Addresses of the map services. Built into the app (data/services.json) and overridable by a copy on the project
/// website, so a provider can be swapped without a release.
/// </summary>
public sealed record ServicesConfig(Uri MapStyle, Uri Geocoder, IReadOnlyList<RouterService> Routers, string Attribution)
{
    private const string ResourceName = "AimOdometer.Data.services.json";

    private static readonly Lazy<ServicesConfig> Embedded = new(() =>
    {
        using var stream = typeof(ServicesConfig).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) ?? throw new InvalidOperationException("The built-in services.json is invalid.");
    });

    /// <summary>The copy shipped with this version.</summary>
    public static ServicesConfig BuiltIn => Embedded.Value;

    /// <summary>The website copy that may override the built-in one.</summary>
    public static Uri RemoteUrl => new($"{AppIdentity.Website}/services.json");

    /// <summary>Parses and validates a config: every address must be https. Null when anything is wrong.</summary>
    public static ServicesConfig? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1)
            {
                return null;
            }

            var routers = root.GetProperty("routers").EnumerateArray()
                .Select(r => new RouterService(Https(r.GetProperty("url").GetString()), r.GetProperty("profile").GetString() ?? "driving"))
                .ToList();
            if (routers.Count == 0 || routers.Any(r => !IsSafeSegment(r.Profile)))
            {
                return null;
            }

            return new ServicesConfig(
                Https(root.GetProperty("mapStyle").GetString()),
                Https(root.GetProperty("geocoder").GetString()),
                routers,
                root.TryGetProperty("attribution", out var a) ? a.GetString() ?? string.Empty : string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static Uri Https(string? text) =>
        Uri.TryCreate(text?.TrimEnd('/'), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new FormatException($"Not an https address: {text}");

    private static bool IsSafeSegment(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Readers for Nominatim and OSRM responses.</summary>
public static class MapJson
{
    /// <summary>Nominatim /search (format=jsonv2) → places.</summary>
    public static IReadOnlyList<Place> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var places = new List<Place>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var display = item.TryGetProperty("display_name", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = display.Split(',')[0].Trim();
            }

            places.Add(new Place(name, display, new GeoPoint(Number(item, "lat"), Number(item, "lon"))));
        }

        return places;
    }

    /// <summary>Nominatim /reverse (format=jsonv2) → the nearest city, town or village name, if any.</summary>
    public static string? ParseReverse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("address", out var address))
        {
            foreach (var key in (ReadOnlySpan<string>)["city", "town", "village", "municipality", "hamlet", "county", "state"])
            {
                if (address.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }

        return root.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n ? n : null;
    }

    /// <summary>
    /// OSRM /route with geometries=polyline6 (or geojson) → distance and line. Null when the server found no route (e.g. across an
    /// ocean); throws on a malformed response.
    /// </summary>
    public static (double Meters, IReadOnlyList<GeoPoint> Line)? ParseRoute(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("code").GetString() != "Ok")
        {
            return null;
        }

        var route = root.GetProperty("routes")[0];
        var geometry = route.GetProperty("geometry");
        var line = geometry.ValueKind == JsonValueKind.String
            ? DecodePolyline(geometry.GetString()!, 6)
            : [.. geometry.GetProperty("coordinates").EnumerateArray().Select(c => new GeoPoint(c[1].GetDouble(), c[0].GetDouble()))];
        if (line.Count < 2)
        {
            return null;
        }

        return (route.GetProperty("distance").GetDouble(), line);
    }

    /// <summary>Decodes an encoded polyline (Google's algorithm; OSRM uses precision 6).</summary>
    public static IReadOnlyList<GeoPoint> DecodePolyline(string encoded, int precision)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var factor = Math.Pow(10, precision);
        var points = new List<GeoPoint>();
        var index = 0;
        long lat = 0, lon = 0;
        while (index < encoded.Length)
        {
            lat += Next(encoded, ref index);
            lon += Next(encoded, ref index);
            points.Add(new GeoPoint(lat / factor, lon / factor));
        }

        return points;

        static long Next(string text, ref int index)
        {
            long result = 0;
            var shift = 0;
            int chunk;
            do
            {
                if (index >= text.Length)
                {
                    throw new FormatException("Truncated polyline.");
                }

                chunk = text[index++] - 63;
                result |= (long)(chunk & 0x1F) << shift;
                shift += 5;
            }
            while (chunk >= 0x20);

            return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
        }
    }

    // Nominatim sends coordinates as strings.
    private static double Number(JsonElement item, string name)
    {
        var value = item.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : double.Parse(value.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>The chosen places, stored as a JSON setting.</summary>
public static class PlaceList
{
    public static string Write(IEnumerable<Place> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var place in places)
            {
                writer.WriteStartObject();
                writer.WriteString("name", place.Name);
                writer.WriteString("display", place.DisplayName);
                writer.WriteNumber("lat", place.Point.Lat);
                writer.WriteNumber("lon", place.Point.Lon);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Empty for a missing or damaged setting.</summary>
    public static IReadOnlyList<Place> Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return [.. doc.RootElement.EnumerateArray().Select(p => new Place(
                p.GetProperty("name").GetString() ?? string.Empty,
                p.GetProperty("display").GetString() ?? string.Empty,
                new GeoPoint(p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble())))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }
}
