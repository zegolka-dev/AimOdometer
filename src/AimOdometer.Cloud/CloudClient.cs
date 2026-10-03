using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;

namespace AimOdometer.Cloud;

/// <summary>A day of the user's statistics summed over all their PCs.</summary>
public sealed record CloudDayTotal(DateOnly Day, double Centimeters, long Clicks, long MoveSeconds);

/// <summary>A PC that syncs to the account.</summary>
public sealed record CloudDevice(Guid PcId, string Name, DateTimeOffset LastSync, double Centimeters);

/// <summary>What the server did with an upload.</summary>
public sealed record SyncResponse(int Accepted, int Rejected);

/// <summary>
/// HTTP client for the AimOdometer cloud: the Steam sign-in exchange, token refresh, Edge Functions and the two read
/// functions. Holds the current session; the access token is refreshed shortly before it expires.
/// </summary>
public sealed class CloudClient : IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly SessionStore _store;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public CloudClient(SessionStore store, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.ProductName}/{AppIdentity.Version}");
        Session = store.Load();
    }

    /// <summary>Raised when the session appears, changes (profile) or ends.</summary>
    public event EventHandler? SessionChanged;

    public CloudSession? Session { get; private set; }

    public bool IsSignedIn => Session is not null;

    /// <summary>Trades the one-time code from the browser (plus the verifier only this app knows) for a session.</summary>
    public async Task<CloudSession> ExchangeAsync(string code, string verifier, CancellationToken cancellation)
    {
        var body = Json(w =>
        {
            w.WriteString("code", code);
            w.WriteString("verifier", verifier);
        });
        using var response = await SendAsync(HttpMethod.Post, CloudConfig.Functions("auth-steam/exchange"), body, bearer: null, cancellation).ConfigureAwait(false);
        var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
        var profile = root.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var session = new CloudSession(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("expires_at").GetInt64()),
            root.GetProperty("user_id").GetString()!,
            Text(profile, "steam_id"),
            Text(profile, "persona_name"),
            Text(profile, "avatar_url"),
            Text(profile, "profile_url"));
        SetSession(session);
        return session;
    }

    /// <summary>The caller's Steam profile (the server refreshes name and avatar from Steam now and then).</summary>
    public async Task<CloudSession> RefreshProfileAsync(CancellationToken cancellation)
    {
        var token = await AccessTokenAsync(cancellation).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Get, CloudConfig.Functions("profile"), null, token, cancellation).ConfigureAwait(false);
        var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
        if (Text(root, "steam_status") is { Length: > 0 } steam and not ("ok" or "fresh"))
        {
            Log.Warning($"Steam profile could not be refreshed on the server: {steam}");
        }

        var session = Session! with
        {
            SteamId = Text(root, "steam_id"),
            PersonaName = Text(root, "persona_name"),
            AvatarUrl = Text(root, "avatar_url"),
            ProfileUrl = Text(root, "profile_url"),
        };
        SetSession(session);
        return session;
    }

    /// <summary>Uploads one request built by <see cref="CloudSync"/>.</summary>
    public async Task<SyncResponse> SyncAsync(string body, CancellationToken cancellation)
    {
        var token = await AccessTokenAsync(cancellation).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Post, CloudConfig.Functions("sync"), body, token, cancellation).ConfigureAwait(false);
        var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
        return new SyncResponse(root.GetProperty("accepted").GetInt32(), root.GetProperty("rejected").GetInt32());
    }

    /// <summary>Daily totals over all PCs from <paramref name="from"/> on.</summary>
    public async Task<IReadOnlyList<CloudDayTotal>> GetDailyTotalsAsync(DateOnly from, CancellationToken cancellation)
    {
        var token = await AccessTokenAsync(cancellation).ConfigureAwait(false);
        var body = Json(w => w.WriteString("p_from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        using var response = await SendAsync(HttpMethod.Post, CloudConfig.Rpc("my_daily_totals"), body, token, cancellation).ConfigureAwait(false);
        var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
        return [.. root.EnumerateArray().Select(r => new CloudDayTotal(
            DateOnly.ParseExact(r.GetProperty("day").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.GetProperty("centimeters").GetDouble(),
            r.GetProperty("clicks").GetInt64(),
            r.GetProperty("move_seconds").GetInt64()))];
    }

    /// <summary>The account's PCs, most recently synced first.</summary>
    public async Task<IReadOnlyList<CloudDevice>> GetDevicesAsync(CancellationToken cancellation)
    {
        var token = await AccessTokenAsync(cancellation).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Post, CloudConfig.Rpc("my_devices"), "{}", token, cancellation).ConfigureAwait(false);
        var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
        return [.. root.EnumerateArray().Select(r => new CloudDevice(
            r.GetProperty("pc_id").GetGuid(),
            r.GetProperty("name").GetString() ?? string.Empty,
            r.GetProperty("last_sync_at").GetDateTimeOffset(),
            r.GetProperty("centimeters").GetDouble()))];
    }

    /// <summary>Deletes the account and everything in the cloud; local statistics stay.</summary>
    public async Task DeleteAccountAsync(CancellationToken cancellation)
    {
        var token = await AccessTokenAsync(cancellation).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Post, CloudConfig.Functions("account-delete"), "{}", token, cancellation).ConfigureAwait(false);
        await ReadAsync(response, cancellation).ConfigureAwait(false);
        SetSession(null);
    }

    /// <summary>Ends the session on the server (best effort) and forgets it here.</summary>
    public async Task SignOutAsync(CancellationToken cancellation)
    {
        if (Session is { } session)
        {
            try
            {
                using var response = await SendAsync(HttpMethod.Post, CloudConfig.Auth("logout?scope=local"), "{}", session.AccessToken, cancellation).ConfigureAwait(false);
            }
            catch (CloudException ex)
            {
                Log.Info($"Sign-out on the server failed (the session ends here anyway): {ex.Error}");
            }
        }

        SetSession(null);
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshGate.Dispose();
    }

    /// <summary>A valid access token, refreshed when it is about to expire. Throws SessionExpired when signed out.</summary>
    internal async Task<string> AccessTokenAsync(CancellationToken cancellation)
    {
        await _refreshGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var session = Session ?? throw new CloudException(CloudError.SessionExpired, "Not signed in.");
            if (session.ExpiresAt - _time.GetUtcNow() > RefreshMargin)
            {
                return session.AccessToken;
            }

            var body = Json(w => w.WriteString("refresh_token", session.RefreshToken));
            HttpResponseMessage response;
            try
            {
                response = await SendAsync(HttpMethod.Post, CloudConfig.Auth("token?grant_type=refresh_token"), body, bearer: null, cancellation).ConfigureAwait(false);
            }
            catch (CloudException ex) when (ex.Error == CloudError.Rejected)
            {
                // Supabase answers 400 for a refresh token that was revoked, used up or belongs to a deleted account.
                SetSession(null);
                throw new CloudException(CloudError.SessionExpired, "The session has ended.", ex);
            }

            using (response)
            {
                var root = await ReadAsync(response, cancellation).ConfigureAwait(false);
                var refreshed = session with
                {
                    AccessToken = root.GetProperty("access_token").GetString()!,
                    RefreshToken = root.GetProperty("refresh_token").GetString()!,
                    ExpiresAt = root.TryGetProperty("expires_at", out var at)
                        ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64())
                        : _time.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()),
                };
                SetSession(refreshed);
                return refreshed.AccessToken;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void SetSession(CloudSession? session)
    {
        Session = session;
        if (session is null)
        {
            _store.Clear();
        }
        else
        {
            _store.Save(session);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri url, string? body, string? bearer, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", CloudConfig.PublishableKey);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudException(CloudError.Offline, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            throw new CloudException(CloudError.Offline, "Timed out.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            var error = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => CloudError.SessionExpired,
                HttpStatusCode.TooManyRequests => CloudError.TooManyRequests,
                HttpStatusCode.BadRequest => CloudError.Rejected,
                _ => CloudError.ServerError,
            };
            if (error == CloudError.SessionExpired && bearer is not null)
            {
                SetSession(null);
            }

            throw new CloudException(error, $"{url.AbsolutePath}: HTTP {(int)response.StatusCode} {Trim(text)}");
        }
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new CloudException(CloudError.ServerError, "Unreadable answer from the server.", ex);
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;

    private static string Trim(string text) => text.Length <= 200 ? text : text[..200];

    internal static string Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
