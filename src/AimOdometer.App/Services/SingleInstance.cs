using System.IO;
using System.Security.Cryptography;
using System.Text;
using AimOdometer.Core;

namespace AimOdometer.App.Services;

/// <summary>
/// One statistics window per session: a second launch (tray click, Start menu) asks the first one to come to the
/// front and exits. A window on an overridden data folder (tests) is its own instance, so it never activates or
/// blocks the real one.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string MutexName = @"Local\AimOdometer.App" + Suffix();
    private static readonly string ActivateEventName = MutexName + ".Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, Action onActivate)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Returns the instance guard, or null when another window already runs (it has been asked to activate).</summary>
    public static SingleInstance? TryAcquire(Action onActivate)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (createdNew)
        {
            return new SingleInstance(mutex, activate, onActivate);
        }

        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    private static string Suffix() => AppIdentity.IsDataDirectoryOverridden
        ? "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppIdentity.DataDirectory).ToUpperInvariant())))[..16]
        : string.Empty;

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
