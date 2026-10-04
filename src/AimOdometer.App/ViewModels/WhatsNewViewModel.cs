using System.IO;
using System.Text.Json;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>One version's changes in the user's language.</summary>
public sealed record WhatsNewRelease(string Title, IReadOnlyList<string> Items);

/// <summary>
/// "What's new", shown once on the first start after an update: the notes of every version newer than the one the user
/// last saw (data/whatsnew.json, newest first). Fresh installs see the onboarding instead.
/// </summary>
public sealed partial class WhatsNewViewModel(IReadOnlyList<WhatsNewRelease> releases, Action close)
{
    public IReadOnlyList<WhatsNewRelease> Releases { get; } = releases;

    public string Title { get; } = Loc.Instance.Format("WhatsNew.Title", AppIdentity.Version);

    [RelayCommand]
    private void Close() => close();

    /// <summary>The window to show now, or null; remembers the current version either way.</summary>
    public static WhatsNewViewModel? ForStart(AppData data, Action close)
    {
        ArgumentNullException.ThrowIfNull(data);
        var current = AppIdentity.Version;
        var lastSeen = data.Setting(SettingKeys.LastSeenVersion);
        data.Store.SetSetting(SettingKeys.LastSeenVersion, current);
        if (lastSeen == current)
        {
            return null;
        }

        // No remembered version: a fresh install (onboarding not done) or a copy from before this window existed.
        if (lastSeen is null && data.Setting(OnboardingViewModel.DoneSettingKey) != "1")
        {
            return null;
        }

        var releases = Load(Path.Combine(AppContext.BaseDirectory, "whatsnew.json"), Loc.Instance.Code, lastSeen);
        return releases.Count == 0 ? null : new WhatsNewViewModel(releases, close);
    }

    /// <summary>Releases newer than <paramref name="lastSeen"/> (the file lists the newest first).</summary>
    internal static List<WhatsNewRelease> Load(string path, string language, string? lastSeen)
    {
        var result = new List<WhatsNewRelease>();
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var release in doc.RootElement.GetProperty("releases").EnumerateArray())
            {
                var version = release.GetProperty("version").GetString() ?? string.Empty;
                if (version == lastSeen)
                {
                    break;
                }

                var items = release.TryGetProperty(language, out var localized) ? localized : release.GetProperty("en");
                result.Add(new WhatsNewRelease(version, [.. items.EnumerateArray().Select(i => i.GetString() ?? string.Empty)]));
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            Core.Diagnostics.Log.Warning($"whatsnew.json is unreadable: {ex.Message}");
        }

        return result;
    }
}
