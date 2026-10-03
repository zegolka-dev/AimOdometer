using System.Text.Json;
using System.Text.Json.Serialization;

namespace AimOdometer.Core.Fun;

/// <summary>A real-world object to compare distance with (data/comparisons.json).</summary>
public sealed record ComparisonObject(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("meters")] double Meters);

public sealed record ComparisonList(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("comparisons")] ComparisonObject[] Comparisons);

/// <summary>How many times a distance fits into an object, e.g. 3.2 Eiffel Towers.</summary>
public sealed record Comparison(ComparisonObject Target, double Ratio);

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ComparisonList))]
internal sealed partial class ComparisonsJsonContext : JsonSerializerContext;

/// <summary>Picks a fun comparison for a distance. Names and phrases are localized in the UI (Cmp.&lt;id&gt;).</summary>
public static class Comparisons
{
    public const string MoonId = "moon";

    private static readonly Lazy<IReadOnlyList<ComparisonObject>> Embedded = new(() =>
    {
        using var stream = typeof(Comparisons).Assembly.GetManifestResourceStream("AimOdometer.Data.comparisons.json")
            ?? throw new InvalidOperationException("Embedded comparisons.json is missing.");
        var list = JsonSerializer.Deserialize(stream, ComparisonsJsonContext.Default.ComparisonList)
            ?? throw new InvalidDataException("comparisons.json is empty.");
        return [.. list.Comparisons.OrderBy(c => c.Meters)];
    });

    public static IReadOnlyList<ComparisonObject> All => Embedded.Value;

    /// <summary>Smallest ratio worth showing: comparisons are printed with one decimal.</summary>
    public const double MinimumRatio = 0.1;

    /// <summary>
    /// The largest object the distance covers at least once (so "3.2 Eiffel Towers", not "0.0001 Moons");
    /// below the smallest object, a fraction of it. Null while that fraction would read as "0.0".
    /// </summary>
    public static Comparison? Best(double centimeters, IReadOnlyList<ComparisonObject>? objects = null)
    {
        var list = objects ?? All;
        var meters = centimeters / 100;
        if (meters <= 0 || list.Count == 0)
        {
            return null;
        }

        var fitting = list.Where(o => meters >= o.Meters && o.Id != MoonId).MaxBy(o => o.Meters) ?? list[0];
        var ratio = meters / fitting.Meters;
        return ratio < MinimumRatio ? null : new Comparison(fitting, ratio);
    }

    /// <summary>Share of the way to the Moon (0..1+).</summary>
    public static double MoonProgress(double centimeters) =>
        centimeters / 100 / (All.FirstOrDefault(o => o.Id == MoonId)?.Meters ?? 384_400_000);
}
