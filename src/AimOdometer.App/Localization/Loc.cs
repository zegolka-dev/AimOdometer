using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;

namespace AimOdometer.App.Localization;

/// <summary>A language available in the i18n folder.</summary>
public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// UI strings from JSON files (i18n/&lt;code&gt;.json next to the exe, plus i18n/ in the data folder for
/// community translations). Adding a language means adding one file. Bindings use the indexer, so switching
/// the language updates every visible string immediately.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string FallbackCode = "en";
    private const string NameKey = "_language";

    private Dictionary<string, string> _strings = [];
    private Dictionary<string, string> _fallback = [];

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed, for view models that format text in code.</summary>
    public event EventHandler? LanguageChanged;

    public string Code { get; private set; } = FallbackCode;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(FallbackCode);

    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : $"[{key}]";

    /// <summary>Folders searched for language files, later ones override earlier ones.</summary>
    public static IEnumerable<string> Folders =>
    [
        Path.Combine(AppContext.BaseDirectory, "i18n"),
        Path.Combine(AppIdentity.DataDirectory, "i18n"),
    ];

    public static IReadOnlyList<LanguageInfo> Available()
    {
        var languages = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                var name = Load(code).GetValueOrDefault(NameKey, code);
                languages[code] = new LanguageInfo(code, name);
            }
        }

        return [.. languages.Values.OrderBy(l => l.NativeName, StringComparer.CurrentCulture)];
    }

    /// <summary>Picks the language for a setting value: "auto"/empty means the Windows UI language if available.</summary>
    public static string Resolve(string? setting)
    {
        var available = Available();
        if (!string.IsNullOrEmpty(setting) && setting != "auto" && available.Any(l => l.Code == setting))
        {
            return setting;
        }

        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return available.Any(l => l.Code == system) ? system : FallbackCode;
    }

    public void SetLanguage(string code)
    {
        _fallback = Load(FallbackCode);
        _strings = code == FallbackCode ? _fallback : Load(code);
        Code = code;
        try
        {
            Culture = CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            Culture = CultureInfo.InvariantCulture;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Formats a localized pattern, e.g. Format("Overview.Streak", 5).</summary>
    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    private static Dictionary<string, string> Load(string code)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Folders.Select(f => Path.Combine(f, code + ".json")).Where(File.Exists))
        {
            try
            {
                foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [])
                {
                    result[key] = value;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Ignoring language file {file}: {ex.Message}");
            }
        }

        return result;
    }
}
