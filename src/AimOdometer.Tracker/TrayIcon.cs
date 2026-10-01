using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>The notification-area icon. All calls must come from the window's thread.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    public const uint CallbackMessage = User32.WmApp + 1;
    private const uint IconId = 1;

    // .NET embeds <ApplicationIcon> as icon group resource 32512 (IDI_APPLICATION).
    private const nint AppIconResourceId = 32512;

    private readonly nint _hwnd;
    private readonly nint _icon;
    private bool _added;

    public TrayIcon(nint hwnd)
    {
        _hwnd = hwnd;
        var instance = Kernel32.GetModuleHandleW(null);
        _icon = User32.LoadImageW(
            instance, AppIconResourceId, User32.ImageIcon,
            User32.GetSystemMetrics(User32.SmCxSmIcon), User32.GetSystemMetrics(User32.SmCySmIcon), User32.LrDefaultColor);
        if (_icon == 0)
        {
            Core.Diagnostics.Log.Warning($"Tray icon resource not found (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
        }
    }

    /// <summary>Adds the icon. Call again after Explorer restarts (TaskbarCreated).</summary>
    public bool Show(string tooltip)
    {
        var data = NewData(Shell32.NifMessage | Shell32.NifIcon | Shell32.NifTip | Shell32.NifShowTip);
        data.UCallbackMessage = CallbackMessage;
        data.HIcon = _icon;
        Shell32.CopyText(data.SzTip, 128, tooltip);

        _added = Shell32.Shell_NotifyIconW(Shell32.NimAdd, &data);
        if (_added)
        {
            data.UVersion = Shell32.NotifyIconVersion4;
            _ = Shell32.Shell_NotifyIconW(Shell32.NimSetVersion, &data);
        }

        return _added;
    }

    public void SetTooltip(string tooltip)
    {
        if (!_added)
        {
            return;
        }

        var data = NewData(Shell32.NifTip | Shell32.NifShowTip);
        Shell32.CopyText(data.SzTip, 128, tooltip);
        _ = Shell32.Shell_NotifyIconW(Shell32.NimModify, &data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            _ = Shell32.Shell_NotifyIconW(Shell32.NimDelete, &data);
            _added = false;
        }

        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
        }
    }

    private Shell32.NotifyIconDataW NewData(uint flags) => new()
    {
        CbSize = (uint)sizeof(Shell32.NotifyIconDataW),
        HWnd = _hwnd,
        UId = IconId,
        UFlags = flags,
    };
}
