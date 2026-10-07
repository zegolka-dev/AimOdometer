using AimOdometer.Core;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// The few strings the tracker shows in its tray icon and menu. The UI uses JSON localization (phase 4);
/// the tracker keeps its own tiny table so it never has to load or parse localization files.
/// </summary>
internal sealed record TrayStrings(
    string TodayFormat,
    string Paused,
    string PausedUntilFormat,
    string OpenStatistics,
    string Mice,
    string DeviceDpiFormat,
    string Pause15Minutes,
    string Pause1Hour,
    string PauseUntilRestart,
    string Resume,
    string StartWithWindows,
    string OpenDataFolder,
    string Exit,
    string FatalStartTitle,
    string AchievementTitle,
    string MoreAchievementsFormat,
    string TestNotificationText,
    string ShameTitle,
    string GearWornTitle,
    string GearWornFormat,
    string ElevatedGameTitle,
    string ElevatedGameFormat,
    UnitLabels Units)
{
    public static readonly TrayStrings English = new(
        TodayFormat: "Today: {0}",
        Paused: "Paused",
        PausedUntilFormat: "Paused until {0:t}",
        OpenStatistics: "Open statistics",
        Mice: "Mice",
        DeviceDpiFormat: "{0} ({1} DPI)",
        Pause15Minutes: "Pause for 15 minutes",
        Pause1Hour: "Pause for 1 hour",
        PauseUntilRestart: "Pause until restart",
        Resume: "Resume",
        StartWithWindows: "Start with Windows",
        OpenDataFolder: "Open data folder",
        Exit: "Exit",
        FatalStartTitle: "AimOdometer could not start",
        AchievementTitle: "New achievement!",
        MoreAchievementsFormat: "{0} and {1} more",
        TestNotificationText: "This is how achievement notifications look.",
        ShameTitle: "Shame achievement!",
        GearWornTitle: "Time to replace your gear",
        GearWornFormat: "{0} has used {1}% of its lifetime",
        ElevatedGameTitle: "AimOdometer can't see your mouse in a game",
        ElevatedGameFormat: "{0} runs as administrator, so its distance is not counted. Click to turn counting on.",
        Units: UnitLabels.English);

    public static readonly TrayStrings Russian = new(
        TodayFormat: "Сегодня: {0}",
        Paused: "На паузе",
        PausedUntilFormat: "Пауза до {0:t}",
        OpenStatistics: "Открыть статистику",
        Mice: "Мыши",
        DeviceDpiFormat: "{0} ({1} DPI)",
        Pause15Minutes: "Пауза на 15 минут",
        Pause1Hour: "Пауза на 1 час",
        PauseUntilRestart: "Пауза до перезапуска",
        Resume: "Продолжить",
        StartWithWindows: "Запускать вместе с Windows",
        OpenDataFolder: "Открыть папку с данными",
        Exit: "Выход",
        FatalStartTitle: "AimOdometer не смог запуститься",
        AchievementTitle: "Новая ачивка!",
        MoreAchievementsFormat: "{0} и ещё {1}",
        TestNotificationText: "Так будут выглядеть уведомления об ачивках.",
        ShameTitle: "Позорная ачивка!",
        GearWornTitle: "Пора менять снаряжение",
        GearWornFormat: "{0}: израсходовано {1}% ресурса",
        ElevatedGameTitle: "AimOdometer не видит мышь в игре",
        ElevatedGameFormat: "«{0}» запущена от имени администратора, поэтому пробег в ней не считается. Нажми, чтобы включить подсчёт.",
        Units: new UnitLabels("см", "м", "км", "дюйм", "фут", "миль"));

    /// <summary>Picks strings by the language setting ("ru", "en") or, for "auto"/empty, by the Windows UI language.</summary>
    public static TrayStrings For(string? language) => Code(language) == "ru" ? Russian : English;

    /// <summary>"ru" or "en" for a language setting ("auto"/empty follows the Windows UI language).</summary>
    public static string Code(string? language)
    {
        const ushort LangRussian = 0x19;
        return language switch
        {
            "ru" => "ru",
            "en" => "en",
            _ => (Kernel32.GetUserDefaultUILanguage() & 0x3FF) == LangRussian ? "ru" : "en",
        };
    }

    // The tracker is built with InvariantGlobalization to stay small; formats are culture-neutral.
    public static System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.InvariantCulture;
}
