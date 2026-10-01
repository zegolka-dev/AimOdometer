using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// Turns raw input device handles into database devices and accumulator slots.
/// Runs only when a new handle appears (once per device per session), never per input event.
/// </summary>
internal sealed unsafe class DeviceResolver(StatsStore store, InputAccumulator accumulator)
{
    private const ushort UsagePageDigitizer = 0x0D;
    private const ushort UsageTouchPad = 0x05;

    private readonly Dictionary<int, DeviceRecord> _slots = [];
    private readonly Dictionary<string, string> _connectedPathByStableKey = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, string> _stableKeyByHandle = [];

    /// <summary>Devices seen in this session, by accumulator slot.</summary>
    public IReadOnlyDictionary<int, DeviceRecord> Slots => _slots;

    public int Resolve(nint handle)
    {
        try
        {
            var (record, kind) = handle == 0 ? ResolveSoftware() : ResolveHardware(handle);

            var slot = accumulator.FindSlotByDeviceId(record.Id);
            if (slot < 0)
            {
                slot = accumulator.AllocateSlot();
                if (slot < 0)
                {
                    Log.Warning($"Too many pointing devices; ignoring '{record.Name}'.");
                    return -1;
                }
            }

            accumulator.ConfigureDevice(slot, record.Id, record.Dpi, countsTowardTotals: !record.Excluded);
            _slots[slot] = record;
            Log.Info($"Device slot {slot}: '{record.Name}' ({kind}, {record.Dpi} DPI) {record.DevicePath}");
            return slot;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error($"Cannot resolve raw input device 0x{handle:X}", ex);
            return -1;
        }
    }

    /// <summary>Called on WM_INPUT_DEVICE_CHANGE removal so a re-plugged mouse is not mistaken for a second one.</summary>
    public void OnDeviceRemoved(nint handle)
    {
        if (_stableKeyByHandle.Remove(handle, out var key))
        {
            _connectedPathByStableKey.Remove(key);
        }
    }

    /// <summary>Re-reads DPI and exclusion flags from the database (after the UI changed them).</summary>
    public void Refresh()
    {
        var byId = store.GetDevices().ToDictionary(d => d.Id);
        foreach (var (slot, old) in _slots.ToArray())
        {
            if (byId.TryGetValue(old.Id, out var current))
            {
                _slots[slot] = current;
                accumulator.ConfigureDevice(slot, current.Id, current.Dpi, countsTowardTotals: !current.Excluded);
            }
        }
    }

    public void SetDpi(int slot, double dpi)
    {
        if (!_slots.TryGetValue(slot, out var record))
        {
            return;
        }

        store.SetDeviceDpi(record.Id, dpi);
        _slots[slot] = record with { Dpi = dpi };
        accumulator.SetDpi(slot, dpi);
    }

    private (DeviceRecord, DeviceKind) ResolveSoftware()
    {
        var record = store.UpsertDevice(
            DevicePath.SoftwareKey, DevicePath.Parse(string.Empty), DeviceKind.Software, null, DateTime.UtcNow);
        return (record, DeviceKind.Software);
    }

    private (DeviceRecord, DeviceKind) ResolveHardware(nint handle)
    {
        var fullPath = GetDeviceName(handle) ?? $"unknown-0x{handle:X}";
        var path = DevicePath.Parse(fullPath);
        var kind = IsTouchpad(path) ? DeviceKind.Touchpad : DeviceKind.Mouse;

        // Two identical mice connected at once share a StableKey: tell them apart by USB instance.
        var key = path.StableKey;
        if (_connectedPathByStableKey.TryGetValue(key, out var otherPath)
            && !string.Equals(otherPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            key = path.UniqueKey;
        }
        else
        {
            _connectedPathByStableKey[key] = fullPath;
            _stableKeyByHandle[handle] = key;
        }

        var record = store.UpsertDevice(key, path, kind, ReadProductName(fullPath), DateTime.UtcNow);
        return (record, kind);
    }

    private static string? GetDeviceName(nint handle)
    {
        uint chars = 0;
        _ = User32.GetRawInputDeviceInfoW(handle, User32.RidiDeviceName, null, &chars);
        if (chars == 0)
        {
            return null;
        }

        var buffer = new char[chars];
        fixed (char* p = buffer)
        {
            var copied = User32.GetRawInputDeviceInfoW(handle, User32.RidiDeviceName, p, &chars);
            return copied is 0 or uint.MaxValue ? null : new string(p).TrimEnd('\0');
        }
    }

    private static string? ReadProductName(string devicePath)
    {
        // Access 0: enough for HidD_* queries and allowed on mice that the system opened exclusively.
        var file = Kernel32.CreateFileW(
            devicePath, 0, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);
        if (file == Kernel32.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            const int MaxChars = 127;
            var buffer = stackalloc char[MaxChars + 1];
            if (!Hid.HidD_GetProductString(file, buffer, MaxChars * sizeof(char)))
            {
                return null;
            }

            buffer[MaxChars] = '\0';
            var name = new string(buffer).Trim();
            return name.Length == 0 ? null : name;
        }
        finally
        {
            Kernel32.CloseHandle(file);
        }
    }

    /// <summary>A mouse collection is a touchpad when the same physical device also exposes a digitizer touch pad.</summary>
    private static bool IsTouchpad(DevicePath mouse)
    {
        uint count = 0;
        var entrySize = (uint)sizeof(User32.RawInputDeviceList);
        if (User32.GetRawInputDeviceList(null, &count, entrySize) == uint.MaxValue || count == 0)
        {
            return false;
        }

        var list = new User32.RawInputDeviceList[count];
        fixed (User32.RawInputDeviceList* p = list)
        {
            var returned = User32.GetRawInputDeviceList(p, &count, entrySize);
            if (returned == uint.MaxValue)
            {
                return false;
            }

            for (var i = 0; i < returned; i++)
            {
                if (p[i].Type != User32.RimTypeHid)
                {
                    continue;
                }

                var info = new User32.RidDeviceInfo { CbSize = (uint)sizeof(User32.RidDeviceInfo) };
                var size = info.CbSize;
                if (User32.GetRawInputDeviceInfoW(p[i].Device, User32.RidiDeviceInfo, &info, &size) == uint.MaxValue
                    || info.HidUsagePage != UsagePageDigitizer
                    || info.HidUsage != UsageTouchPad)
                {
                    continue;
                }

                var name = GetDeviceName(p[i].Device);
                if (name is not null && DevicePath.Parse(name).SiblingKey == mouse.SiblingKey)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
