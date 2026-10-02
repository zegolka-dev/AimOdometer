using AimOdometer.Core;
using AimOdometer.Core.Games;
using AimOdometer.Core.Ipc;
using AimOdometer.Core.Storage;

namespace AimOdometer.App.Services;

/// <summary>
/// The window's access to the local database (shared with the tracker through SQLite WAL) and to settings.
/// Every change that the tracker must know about is followed by a ReloadSettings pipe command.
/// </summary>
public sealed class AppData : IDisposable
{
    private GameCatalog? _catalog;

    public AppData(StatsStore store) => Store = store;

    public StatsStore Store { get; }

    /// <summary>Raised when statistics-relevant data changed in this window (DPI, rules, names, devices).</summary>
    public event EventHandler? Changed;

    public GameCatalog Catalog => _catalog ??= GameCatalog.Create(Store);

    public string? Setting(string key) => Store.GetSetting(key);

    public void SetSetting(string key, string value)
    {
        Store.SetSetting(key, value);
        _ = TrackerConnection.SendAsync(TrackerCommand.ReloadSettings);
    }

    public UnitSystem Units => DistanceFormat.ParseUnits(Setting(SettingKeys.Units));

    /// <summary>Call after changing rules, names, DPI or device flags.</summary>
    public void NotifyChanged(bool reloadTracker)
    {
        _catalog = null;
        if (reloadTracker)
        {
            _ = TrackerConnection.SendAsync(TrackerCommand.ReloadSettings);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Store.Dispose();
}
