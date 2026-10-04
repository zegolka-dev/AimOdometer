using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Games;

namespace AimOdometer.App.Services;

/// <summary>Export of the statistics to CSV, and deletion of everything AimOdometer keeps on this PC.</summary>
public static class LocalData
{
    /// <summary>Command-line flag of the window restarted after a deletion: it waits for the old window to close.</summary>
    public const string RestartFlag = "--after-reset";

    private static string Marker => Path.Combine(AppIdentity.DataDirectory, "reset.request");

    /// <summary>
    /// Daily totals per game and mouse as CSV, in the user's regional format (list separator, decimals) so the file
    /// opens correctly in Excel.
    /// </summary>
    public static void ExportCsv(AppData data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);
        var culture = CultureInfo.CurrentCulture;
        var sep = culture.TextInfo.ListSeparator;
        var apps = data.Store.GetApps().ToDictionary(a => a.Id, a => a.ExePath);
        var mice = data.Store.GetDevices().ToDictionary(d => d.Id, d => d.DisplayName ?? d.ProductName ?? d.DeviceKey);
        var names = new Dictionary<long, string>();
        string GameOf(long appId)
        {
            if (!names.TryGetValue(appId, out var name))
            {
                var c = apps.TryGetValue(appId, out var exe) ? data.Catalog.Classify(appId, exe) : AppClassification.Other;
                name = c.Category == AppCategory.Game && c.Game is { } g ? g.Name : string.Empty;
                names[appId] = name;
            }

            return name;
        }

        var rows = data.Store.GetDailyBreakdown()
            .GroupBy(b => (b.Date, Game: GameOf(b.AppId), Mouse: mice.GetValueOrDefault(b.DeviceId, string.Empty)))
            .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Game, StringComparer.CurrentCulture);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(sep, "date", "game", "mouse", "meters", "clicks", "seconds_moving", "fastest_flick_m_s"));
        foreach (var g in rows)
        {
            csv.AppendLine(string.Join(sep,
                g.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Quote(g.Key.Game, sep),
                Quote(g.Key.Mouse, sep),
                (g.Sum(b => b.Centimeters) / 100).ToString("0.00", culture),
                g.Sum(b => b.Clicks).ToString(culture),
                g.Sum(b => b.MoveSeconds).ToString(culture),
                (g.Max(b => b.PeakSpeed) / 100).ToString("0.00", culture)));
        }

        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// Deletes all local data: stops the tracker (it holds the database), leaves a marker and restarts the window,
    /// which deletes everything but the logs before opening the database again (see <see cref="ApplyIfRequested"/>).
    /// </summary>
    public static async Task DeleteAllAndRestartAsync()
    {
        if (!AppIdentity.IsDataDirectoryOverridden)
        {
            await Updates.StopTrackerAsync(); // a test window never stops the real tracker
        }
        File.WriteAllText(Marker, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, RestartFlag) { UseShellExecute = false })?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Runs at start-up, before the database is opened: carries out a requested deletion.</summary>
    public static void ApplyIfRequested()
    {
        if (!File.Exists(Marker))
        {
            return;
        }

        var folder = AppIdentity.DataDirectory;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (string.Equals(Path.GetFileName(entry), "logs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Retry(() =>
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            });
        }

        Log.Warning("All local statistics and settings were deleted at the user's request");
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < 25)
            {
                Thread.Sleep(200); // the old window or the tracker is still letting go of a file
            }
            catch (UnauthorizedAccessException) when (attempt < 25)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static string Quote(string value, string separator) =>
        value.Contains(separator, StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
