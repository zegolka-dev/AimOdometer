namespace AimOdometer.Cloud;

/// <summary>
/// The Supabase project. The publishable key is meant to be public (it is in every client); what a caller may do is
/// decided by row level security and the Edge Functions, never by this key. Secrets (the Steam Web API key, the
/// service role) exist only on the server.
/// </summary>
public static class CloudConfig
{
    public static readonly Uri ProjectUrl = new("https://tphrgryvyxgldozymqzk.supabase.co");

    public const string PublishableKey = "sb_publishable_eEwQA9Obcj7caVPS2vIi3g_tWKlxo4W";

    public static Uri Functions(string path) => new(ProjectUrl, $"functions/v1/{path}");

    public static Uri Auth(string path) => new(ProjectUrl, $"auth/v1/{path}");

    public static Uri Rpc(string function) => new(ProjectUrl, $"rest/v1/rpc/{function}");
}
