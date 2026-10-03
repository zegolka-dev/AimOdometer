using System.Text.Json;
using AimOdometer.Core;
using AimOdometer.Win32;

namespace AimOdometer.Cloud;

/// <summary>A signed-in Supabase session and the Steam profile behind it.</summary>
public sealed record CloudSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string UserId,
    string SteamId,
    string PersonaName,
    string AvatarUrl,
    string ProfileUrl);

/// <summary>Why a cloud call failed, for the message shown to the user.</summary>
public enum CloudError
{
    Offline,
    SignInCancelled,
    SignInDenied,
    SignInTimedOut,
    BrowserFailed,
    SessionExpired,
    TooManyRequests,
    Rejected,      // the server refused the request as invalid (e.g. an expired sign-in)
    ServerError,
}

public sealed class CloudException(CloudError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public CloudError Error { get; } = error;
}

/// <summary>
/// The session on disk, encrypted with DPAPI for the current Windows user (another account or another PC cannot read
/// it). Lives in the data folder, so uninstalling and reinstalling keeps you signed in.
/// </summary>
public sealed class SessionStore(string path)
{
    private static readonly byte[] Entropy = "AimOdometer.Cloud.Session.v1"u8.ToArray();

    public static string DefaultPath => System.IO.Path.Combine(AppIdentity.DataDirectory, "cloud", "session.dat");

    public string Path { get; } = path;

    public CloudSession? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var json = Dpapi.Unprotect(File.ReadAllBytes(Path), Entropy);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new CloudSession(
                r.GetProperty("access").GetString()!,
                r.GetProperty("refresh").GetString()!,
                DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("expires").GetInt64()),
                r.GetProperty("user").GetString()!,
                r.GetProperty("steam").GetString()!,
                r.GetProperty("name").GetString() ?? string.Empty,
                r.GetProperty("avatar").GetString() ?? string.Empty,
                r.GetProperty("profile").GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is CryptographicDataException or JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            // Another Windows user's file, or damaged: behave as signed out.
            return null;
        }
    }

    public void Save(CloudSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("access", session.AccessToken);
            w.WriteString("refresh", session.RefreshToken);
            w.WriteNumber("expires", session.ExpiresAt.ToUnixTimeSeconds());
            w.WriteString("user", session.UserId);
            w.WriteString("steam", session.SteamId);
            w.WriteString("name", session.PersonaName);
            w.WriteString("avatar", session.AvatarUrl);
            w.WriteString("profile", session.ProfileUrl);
            w.WriteEndObject();
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, Dpapi.Protect(buffer.ToArray(), Entropy));
        File.Move(temp, Path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
