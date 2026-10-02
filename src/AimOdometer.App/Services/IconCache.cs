using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AimOdometer.App.Services;

/// <summary>Icons extracted from executables (SHGetFileInfo), cached for the lifetime of the window.</summary>
public static unsafe partial class IconCache
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, ShFileInfo* info, uint size, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    /// <summary>The exe's icon, or null when the file is gone or has no icon (caller shows a placeholder).</summary>
    public static ImageSource? Get(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !Path.IsPathRooted(exePath))
        {
            return null;
        }

        if (Cache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }

        ImageSource? image = null;
        if (File.Exists(exePath))
        {
            var info = default(ShFileInfo);
            if (SHGetFileInfoW(exePath, 0, &info, (uint)sizeof(ShFileInfo), ShgfiIcon | ShgfiLargeIcon) != 0 && info.Icon != 0)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    image = source;
                }
                finally
                {
                    DestroyIcon(info.Icon);
                }
            }
        }

        Cache[exePath] = image;
        return image;
    }
}
