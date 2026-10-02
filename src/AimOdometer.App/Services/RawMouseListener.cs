using System.Windows;
using System.Windows.Interop;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.App.Services;

/// <summary>One raw mouse report received while the window is in the foreground.</summary>
public readonly record struct RawMouseReport(nint Device, int Dx, int Dy, ushort ButtonFlags, bool Absolute)
{
    public const ushort LeftDown = 0x0001;
    public const ushort LeftUp = 0x0002;
}

/// <summary>
/// Reads raw mouse input for the window while it is focused (full polling rate, per device). Used by the DPI
/// calibration and to let the user pick a mouse by moving it. Lives only while those screens are open.
/// </summary>
public sealed unsafe class RawMouseListener : IDisposable
{
    private readonly HwndSource _source;
    private readonly byte[] _buffer = new byte[128];

    public RawMouseListener(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _source = (HwndSource)PresentationSource.FromVisual(window)!;
        _source.AddHook(WndProc);
        var device = new User32.RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0, HwndTarget = _source.Handle };
        User32.RegisterRawInputDevices(&device, 1, (uint)sizeof(User32.RawInputDevice));
    }

    public event Action<RawMouseReport>? Report;

    /// <summary>Finds the database device for a raw input handle (the tracker registers devices on first input).</summary>
    public static DeviceRecord? FindDevice(StatsStore store, nint handle)
    {
        ArgumentNullException.ThrowIfNull(store);
        var name = DeviceName(handle);
        if (name is null)
        {
            return null;
        }

        var path = DevicePath.Parse(name);
        var devices = store.GetDevices();
        return devices.FirstOrDefault(d => d.DeviceKey == path.UniqueKey)
            ?? devices.FirstOrDefault(d => d.DeviceKey == path.StableKey);
    }

    public static string? DeviceName(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        uint chars = 0;
        _ = User32.GetRawInputDeviceInfoW(handle, User32.RidiDeviceName, null, &chars);
        if (chars == 0)
        {
            return null;
        }

        var text = new char[chars];
        fixed (char* p = text)
        {
            return User32.GetRawInputDeviceInfoW(handle, User32.RidiDeviceName, p, &chars) is 0 or uint.MaxValue
                ? null
                : new string(p).TrimEnd('\0');
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != (int)User32.WmInput)
        {
            return 0;
        }

        uint size = (uint)_buffer.Length;
        fixed (byte* p = _buffer)
        {
            if (User32.GetRawInputData(lParam, User32.RidInput, p, &size, User32.RawInputHeaderSize) is 0 or uint.MaxValue
                || *(uint*)p != User32.RimTypeMouse)
            {
                return 0;
            }

            var mouse = p + User32.RawInputHeaderSize;
            Report?.Invoke(new RawMouseReport(
                Device: *(nint*)(p + 8),
                Dx: *(int*)(mouse + 12),
                Dy: *(int*)(mouse + 16),
                ButtonFlags: *(ushort*)(mouse + 4),
                Absolute: (*(ushort*)mouse & 1) != 0));
        }

        return 0;
    }

    public void Dispose()
    {
        var device = new User32.RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = User32.RidevRemove, HwndTarget = 0 };
        User32.RegisterRawInputDevices(&device, 1, (uint)sizeof(User32.RawInputDevice));
        _source.RemoveHook(WndProc);
    }
}
