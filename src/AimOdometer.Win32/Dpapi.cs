using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>
/// Windows Data Protection (DPAPI), current-user scope: data encrypted here can only be decrypted by the same Windows
/// user on the same PC. Used for the cloud session tokens.
/// </summary>
public static unsafe partial class Dpapi
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy) => Run(data, entropy, protect: true);

    /// <summary>Throws <see cref="CryptographicDataException"/> if the data was protected by another user or is damaged.</summary>
    public static byte[] Unprotect(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy) => Run(data, entropy, protect: false);

    private static byte[] Run(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy, bool protect)
    {
        fixed (byte* input = data)
        fixed (byte* salt = entropy)
        {
            var inBlob = new DataBlob { Size = (uint)data.Length, Data = input };
            var saltBlob = new DataBlob { Size = (uint)entropy.Length, Data = salt };
            var outBlob = default(DataBlob);
            var ok = protect
                ? CryptProtectData(&inBlob, null, &saltBlob, 0, 0, CryptProtectUiForbidden, &outBlob)
                : CryptUnprotectData(&inBlob, 0, &saltBlob, 0, 0, CryptProtectUiForbidden, &outBlob);
            if (!ok)
            {
                throw new CryptographicDataException(new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }

            try
            {
                return new ReadOnlySpan<byte>(outBlob.Data, (int)outBlob.Size).ToArray();
            }
            finally
            {
                LocalFree((nint)outBlob.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint Size;
        public byte* Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DataBlob* data, string? description, DataBlob* entropy, nint reserved, nint prompt, uint flags, DataBlob* output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DataBlob* data, nint description, DataBlob* entropy, nint reserved, nint prompt, uint flags, DataBlob* output);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}

/// <summary>DPAPI could not decrypt (another Windows user, another PC, or damaged data).</summary>
public sealed class CryptographicDataException(string message) : Exception(message);
