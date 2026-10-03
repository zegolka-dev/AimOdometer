using System.Text.Json;
using System.Text.Json.Serialization;
using AimOdometer.Core.Diagnostics;

namespace AimOdometer.Core;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class StringTableJsonContext : JsonSerializerContext;

/// <summary>
/// Reads a language file (i18n/&lt;code&gt;.json) without reflection, so the NativeAOT tracker can show the same
/// localized names (e.g. achievements) as the window.
/// </summary>
public static class StringTable
{
    /// <summary>Strings from i18n/&lt;code&gt;.json under <paramref name="baseDirectory"/>, falling back to English; empty when missing.</summary>
    public static IReadOnlyDictionary<string, string> Load(string baseDirectory, string code)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in code == "en" ? ["en"] : new[] { "en", code })
        {
            var path = Path.Combine(baseDirectory, "i18n", language + ".json");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                foreach (var (key, value) in JsonSerializer.Deserialize(File.ReadAllText(path), StringTableJsonContext.Default.DictionaryStringString) ?? [])
                {
                    result[key] = value;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Cannot read {path}: {ex.Message}");
            }
        }

        return result;
    }
}
