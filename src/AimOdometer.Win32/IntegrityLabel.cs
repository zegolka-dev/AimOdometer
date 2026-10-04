using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>
/// Mandatory integrity labels. An object created by a process running as administrator gets the high integrity label,
/// and normal (medium) processes cannot write to it. Lowering the label of our own pipe to medium lets the normal
/// window talk to an elevated tracker; the access list still allows only the current user.
/// </summary>
public static unsafe partial class IntegrityLabel
{
    private const uint SddlRevision1 = 1;
    private const int SeKernelObject = 6;
    private const uint LabelSecurityInformation = 0x10;

    /// <summary>Sets the medium label (no write up from below medium). The handle needs WRITE_OWNER access.</summary>
    public static bool SetMedium(SafeHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        nint descriptor = 0;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("S:(ML;;NW;;;ME)", SddlRevision1, &descriptor, null))
        {
            return false;
        }

        try
        {
            int present, defaulted;
            nint sacl;
            if (!GetSecurityDescriptorSacl(descriptor, &present, &sacl, &defaulted) || present == 0)
            {
                return false;
            }

            var added = false;
            handle.DangerousAddRef(ref added);
            try
            {
                return SetSecurityInfo(handle.DangerousGetHandle(), SeKernelObject, LabelSecurityInformation, 0, 0, 0, sacl) == 0;
            }
            finally
            {
                if (added)
                {
                    handle.DangerousRelease();
                }
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, nint* descriptor, uint* size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSecurityDescriptorSacl(nint descriptor, int* present, nint* sacl, int* defaulted);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(nint handle, int objectType, uint securityInfo, nint owner, nint group, nint dacl, nint sacl);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
