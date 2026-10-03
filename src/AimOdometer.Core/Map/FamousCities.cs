using System.Text.Json;

namespace AimOdometer.Core.Map;

/// <summary>A well-known city; its name is localized with the key <c>City.{Id}</c>.</summary>
public sealed record FamousCity(string Id, GeoPoint Point);

/// <summary>
/// Destinations for the "random direction" walk shown as soon as the user picked a start city
/// (data/cities.json: capitals and cities everybody knows, on every continent).
/// </summary>
public static class FamousCities
{
    /// <summary>Closer than this, a destination is not much of a journey.</summary>
    public const double MinimumMeters = 300_000;

    private const string ResourceName = "AimOdometer.Data.cities.json";

    private static readonly Lazy<IReadOnlyList<FamousCity>> Embedded = new(() =>
    {
        using var stream = typeof(FamousCities).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var doc = JsonDocument.Parse(stream);
        return [.. doc.RootElement.GetProperty("cities").EnumerateArray().Select(c => new FamousCity(
            c.GetProperty("id").GetString()!,
            new GeoPoint(c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble())))];
    });

    public static IReadOnlyList<FamousCity> All => Embedded.Value;

    public static FamousCity? Find(string? id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// A random city that is a real journey from <paramref name="start"/> and not reached yet: at least
    /// <see cref="MinimumMeters"/> and a bit more than <paramref name="walkedMeters"/> as the crow flies, but not absurdly
    /// far (so it is usually on the same continent, with roads). Cities in <paramref name="except"/> are skipped while
    /// there is a choice.
    /// </summary>
    public static FamousCity Pick(GeoPoint start, double walkedMeters, Random random, IEnumerable<string>? except = null, IReadOnlyList<FamousCity>? cities = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        var list = cities ?? All;
        var near = Math.Max(MinimumMeters, walkedMeters * 1.1);
        var far = Math.Max(near * 3, 2_500_000);
        var byDistance = list.Select(c => (City: c, Meters: Geo.Distance(start, c.Point))).OrderBy(c => c.Meters).ToList();

        var candidates = byDistance.Where(c => c.Meters >= near && c.Meters <= far).Select(c => c.City).ToList();
        if (candidates.Count < 2)
        {
            // Nothing in the window (a very long walk, or a remote start): the closest few beyond it, else the farthest.
            candidates = [.. byDistance.Where(c => c.Meters >= near).Take(5).Select(c => c.City)];
            if (candidates.Count == 0)
            {
                candidates = [byDistance[^1].City];
            }
        }

        var skip = except?.ToHashSet() ?? [];
        if (candidates.Any(c => !skip.Contains(c.Id)))
        {
            candidates.RemoveAll(c => skip.Contains(c.Id));
        }

        return candidates[random.Next(candidates.Count)];
    }
}
