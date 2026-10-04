using System.Globalization;
using AimOdometer.Core.Diagnostics;

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
    private static readonly string[] JournalSuffixes = ["-wal", "-shm"];

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

    /// <summary>
    /// Opens the statistics database. If SQLite reports it damaged (a power cut mid-write, a bad disk), the damaged file
    /// is kept next to it as "aimodometer.db.broken-&lt;time&gt;" and the newest readable daily backup takes its place,
    /// or an empty database when there is none. The window reads <see cref="SettingKeys.RecoveredFrom"/> to tell the
    /// user. Nothing is deleted.
    /// </summary>
    public static StatsStore OpenOrRecover(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return StatsStore.Open(path);
        }
        catch (SqliteException ex) when (IsDamaged(ex))
        {
            Log.Error("The statistics database is damaged; restoring the newest backup", ex);
        }

        var broken = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var restore = path + ".restore";
        DeleteDatabaseFiles(restore);

        string? from = null;
        foreach (var backup in List(FolderFor(Path.GetDirectoryName(Path.GetFullPath(path))!)))
        {
            try
            {
                File.Copy(backup, restore);
                StatsStore.Open(restore).Dispose();
                from = Path.GetFileNameWithoutExtension(backup)[Prefix.Length..];
                break;
            }
            catch (SqliteException ex) when (IsDamaged(ex))
            {
                Log.Warning($"Backup {Path.GetFileName(backup)} is damaged too: {ex.Message}");
                DeleteDatabaseFiles(restore);
            }
        }

        if (from is null)
        {
            StatsStore.Open(restore).Dispose();
        }

        // The damaged file's journal must not be replayed into the restored one.
        foreach (var suffix in JournalSuffixes)
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, broken + suffix);
            }
        }

        File.Replace(restore, path, broken); // atomic: the path never stops existing for the other process
        var store = StatsStore.Open(path);
        store.SetSetting(SettingKeys.RecoveredFrom, from ?? "none");
        Log.Warning($"Statistics database restored from {from ?? "nothing (no readable backup)"}; damaged copy: {broken}");
        return store;
    }

    /// <summary>SQLITE_CORRUPT (11) or SQLITE_NOTADB (26), including extended codes.</summary>
    public static bool IsDamaged(SqliteException ex) => ex is not null && (ex.ErrorCode & 0xFF) is 11 or 26;

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var file in JournalSuffixes.Select(s => path + s).Prepend(path))
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>Backup files, newest first (by the date in the name).</summary>
    public static IReadOnlyList<string> List(string folder) => !Directory.Exists(folder)
        ? []
        : [.. Directory.EnumerateFiles(folder, Prefix + "????-??-??" + Extension)
            .Where(f => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f)[Prefix.Length..], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(f => f, StringComparer.Ordinal)];
}
