using System.Globalization;

namespace AimOdometer.Core.Input;

/// <summary>What kind of pointing device produced the input.</summary>
public enum DeviceKind
{
    Mouse = 0,
    Touchpad = 1,

    /// <summary>Input injected by software (SendInput, remote tools); raw input reports no device handle.</summary>
    Software = 2,
}

/// <summary>
/// Parses Windows HID device interface paths such as
/// <c>\\?\HID#VID_046D&amp;PID_C547&amp;MI_02&amp;Col01#8&amp;2a4b1c3&amp;0&amp;0000#{378de44c-...}</c>.
/// </summary>
public sealed record DevicePath(string FullPath, string StableKey, string SiblingKey, int? VendorId, int? ProductId)
{
    /// <summary>Key used for raw input that carries no device handle (injected input).</summary>
    public const string SoftwareKey = "SOFTWARE";

    public static DevicePath Parse(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        var segments = fullPath.Split('#');

        // segments: [\\?\HID, hardware id, instance id, interface class guid]
        var hardwareId = segments.Length > 1 ? segments[0] + "#" + segments[1] : fullPath;
        var instance = segments.Length > 2 ? segments[2] : string.Empty;

        var withoutCollection = RemoveCollection(hardwareId).ToUpperInvariant();
        var instanceParent = instance.Contains('&', StringComparison.Ordinal)
            ? instance[..instance.LastIndexOf('&')]
            : instance;

        return new DevicePath(
            fullPath,
            StableKey: withoutCollection.TrimStart('\\', '?'),
            SiblingKey: (withoutCollection.TrimStart('\\', '?') + "#" + instanceParent).ToUpperInvariant(),
            VendorId: FindHexId(hardwareId, "VID"),
            ProductId: FindHexId(hardwareId, "PID"));
    }

    /// <summary>
    /// Key that also includes the USB instance. Used only when two devices with the same <see cref="StableKey"/>
    /// (two identical mice) are connected at the same time.
    /// </summary>
    public string UniqueKey
    {
        get
        {
            var segments = FullPath.Split('#');
            return segments.Length > 2 ? StableKey + "#" + segments[2].ToUpperInvariant() : StableKey;
        }
    }

    /// <summary>Removes the "&amp;ColNN" top-level-collection suffix shared by all functions of one device.</summary>
    private static string RemoveCollection(string hardwareId)
    {
        var index = hardwareId.IndexOf("&Col", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return hardwareId;
        }

        var end = index + 4;
        while (end < hardwareId.Length && char.IsAsciiHexDigit(hardwareId[end]))
        {
            end++;
        }

        return hardwareId.Remove(index, end - index);
    }

    /// <summary>
    /// Finds "VID_046D" (USB) or "VID&amp;0002046d" (Bluetooth, vendor-source prefix) and returns the last 4 hex digits.
    /// </summary>
    private static int? FindHexId(string text, string name)
    {
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var digitsStart = index + name.Length;
            if (digitsStart < text.Length && text[digitsStart] is '_' or '&')
            {
                digitsStart++;
                var end = digitsStart;
                while (end < text.Length && char.IsAsciiHexDigit(text[end]))
                {
                    end++;
                }

                if (end - digitsStart >= 4)
                {
                    return int.Parse(text.AsSpan(end - 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }

            start = index + name.Length;
        }
    }
}
