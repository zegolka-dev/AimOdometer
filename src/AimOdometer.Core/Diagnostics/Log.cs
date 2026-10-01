using System.Globalization;

namespace AimOdometer.Core.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Tiny file logger with size-based rotation (current file + one backup).
/// Writes are synchronous and rare by design: never log from the input hot path.
/// </summary>
public static class Log
{
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    private static readonly Lock Gate = new();
    private static string? _path;
    private static long _maxBytes = DefaultMaxBytes;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    /// <summary>Directs log output to <paramref name="path"/>. Until called, logging is a no-op.</summary>
    public static void Configure(string path, LogLevel minimumLevel, long maxBytes = DefaultMaxBytes)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _path = path;
            _maxBytes = maxBytes;
            MinimumLevel = minimumLevel;
        }
    }

    public static bool TryParseLevel(string? text, out LogLevel level) =>
        Enum.TryParse(text, ignoreCase: true, out level) && Enum.IsDefined(level);

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warning(string message) => Write(LogLevel.Warning, message);

    public static void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, exception is null ? message : $"{message}: {exception}");

    public static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel || _path is null)
        {
            return;
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length + line.Length > _maxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(_path, line);
            }
            catch (IOException)
            {
                // Logging must never take the process down (disk full, file locked by an editor, ...).
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
