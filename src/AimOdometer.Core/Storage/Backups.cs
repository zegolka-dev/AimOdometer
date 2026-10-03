using System.Globalization;

namespace AimOdometer.Core.Storage;

/// <summary>
/// Daily copies of the statistics database in &lt;data folder&gt;\backups, newest seven kept.
/// A consistent copy is made with SQLite's VACUUM INTO, so it is safe while the tracker is writing.
/// </summary>
public static class Backups
{
    public const int DefaultKeep = 7;
    private const string Prefix = "aimodometer-";
    private const string Extension = ".db";

    public static string FolderFor(string dataDirectory) => Path.Combine(dataDirectory, "backups");

    /// <summary>
    /// Creates today's backup unless it exists, then removes the oldest beyond <paramref name="keep"/>.
    /// Returns the path written, or null when today's backup already existed.
    /// </summary>
    public static string? CreateDaily(StatsStore store, string folder, DateOnly today, int keep = DefaultKeep)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, Prefix + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension);
        string? written = null;
        if (!File.Exists(path))
        {
            var temp = path + ".tmp";
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            store.BackupTo(temp);
            File.Move(temp, path); // a half-written backup never carries the final name
            written = path;
        }

        foreach (var old in List(folder).Skip(keep))
        {
            File.Delete(old);
        }

        return written;
    }

    /// <summary>Backup files, newest first (by the date in the name).</summary>
    public static IReadOnlyList<string> List(string folder) => !Directory.Exists(folder)
        ? []
        : [.. Directory.EnumerateFiles(folder, Prefix + "????-??-??" + Extension)
            .Where(f => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f)[Prefix.Length..], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(f => f, StringComparer.Ordinal)];
}
