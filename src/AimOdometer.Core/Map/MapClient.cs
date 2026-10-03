using System.Globalization;
using System.Net;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Map;

/// <summary>A map service could not be reached or answered with an error.</summary>
public sealed class MapServiceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Geocoding (Nominatim) and routing (OSRM) for the Map tab. Follows the services' usage policies: an identifying
/// User-Agent, at most one request per second per host, every answer cached in the local database (search results and
/// routes never expire), no search-as-you-type. Only the statistics window uses this, and only on the Map tab.
/// </summary>
public sealed class MapClient : IDisposable
{
    /// <summary>Minimum time between two requests to the same host (policies say at most 1 per second).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);

    /// <summary>How long the website copy of services.json is trusted before it is checked again.</summary>
    public static readonly TimeSpan ServicesMaxAge = TimeSpan.FromDays(1);

    // Shared by every client in the process, so reopening the tab cannot double the request rate.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastRequest = new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _http;
    private readonly StatsStore _store;
    private readonly TimeProvider _time;

    public MapClient(StatsStore store, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public static string UserAgent => $"{AppIdentity.ProductName}/{AppIdentity.Version} (+{AppIdentity.Repository})";

    /// <summary>Service addresses in use: built in until <see cref="LoadServicesAsync"/> found a newer website copy.</summary>
    public ServicesConfig Services { get; private set; } = ServicesConfig.BuiltIn;

    /// <summary>How long to wait before the next request to a host.</summary>
    public static TimeSpan WaitTime(DateTimeOffset? lastRequest, DateTimeOffset now)
    {
        if (lastRequest is not { } last)
        {
            return TimeSpan.Zero;
        }

        var wait = MinInterval - (now - last);
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>Uses the website's services.json when it is valid (checked at most once a day), otherwise the built-in one.</summary>
    public async Task<ServicesConfig> LoadServicesAsync(CancellationToken cancellation)
    {
        const string Kind = "services";
        const string Key = "remote";
        var cached = _store.GetGeoCache(Kind, Key);
        if (cached is { } c && _time.GetUtcNow() - c.FetchedAt < ServicesMaxAge)
        {
            return Services = ServicesConfig.Parse(c.Value) ?? ServicesConfig.BuiltIn;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var (status, body) = await GetAsync(ServicesConfig.RemoteUrl, timeout.Token).ConfigureAwait(false);
            if (status == HttpStatusCode.OK && ServicesConfig.Parse(body) is { } remote)
            {
                _store.SetGeoCache(Kind, Key, body, _time.GetUtcNow());
                return Services = remote;
            }

            // Not published or invalid: remember that for a day too, so the check stays one request a day.
            _store.SetGeoCache(Kind, Key, "{}", _time.GetUtcNow());
        }
        catch (MapServiceException ex)
        {
            Log.Info($"services.json not loaded: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            Log.Info("services.json not loaded: timed out");
        }

        return Services = cached is { } old ? ServicesConfig.Parse(old.Value) ?? ServicesConfig.BuiltIn : ServicesConfig.BuiltIn;
    }

    /// <summary>Places matching the text (up to 5), names in <paramref name="language"/>.</summary>
    public async Task<IReadOnlyList<Place>> SearchAsync(string query, string language, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0)
        {
            return [];
        }

        var key = $"{language}|{text.ToLowerInvariant()}";
        var body = await CachedAsync("search", key, async () =>
        {
            var url = new Uri($"{Services.Geocoder}/search?format=jsonv2&limit=5&q={Uri.EscapeDataString(text)}&accept-language={Uri.EscapeDataString(language)}");
            var (status, response) = await GetAsync(url, cancellation).ConfigureAwait(false);
            return status == HttpStatusCode.OK ? response : throw new MapServiceException($"Search failed: HTTP {(int)status}");
        }).ConfigureAwait(false);
        return MapJson.ParseSearch(body);
    }

    /// <summary>The city, town or village at a point (cached per ~1 km), or null at sea.</summary>
    public async Task<string?> PlaceNameAtAsync(GeoPoint point, string language, CancellationToken cancellation)
    {
        var p = point.Normalize();
        var lat = p.Lat.ToString("0.00", CultureInfo.InvariantCulture);
        var lon = p.Lon.ToString("0.00", CultureInfo.InvariantCulture);
        var body = await CachedAsync("reverse", $"{language}|{lat},{lon}", async () =>
        {
            var url = new Uri($"{Services.Geocoder}/reverse?format=jsonv2&zoom=10&lat={lat}&lon={lon}&accept-language={Uri.EscapeDataString(language)}");
            var (status, response) = await GetAsync(url, cancellation).ConfigureAwait(false);
            return status == HttpStatusCode.OK ? response : throw new MapServiceException($"Reverse geocoding failed: HTTP {(int)status}");
        }).ConfigureAwait(false);
        return MapJson.ParseReverse(body);
    }

    /// <summary>
    /// The walking route between two places. Falls back to a straight line when no router has a road route (an ocean
    /// in between: remembered) or none is reachable right now (not remembered, retried next time).
    /// </summary>
    public async Task<RouteLeg> RouteAsync(Place from, Place to, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var coordinates = string.Create(CultureInfo.InvariantCulture,
            $"{from.Point.Lon:0.#####},{from.Point.Lat:0.#####};{to.Point.Lon:0.#####},{to.Point.Lat:0.#####}");
        const string Kind = "route-full";
        if (_store.GetGeoCache(Kind, coordinates) is { } cached)
        {
            return ToLeg(from, to, cached.Value);
        }

        foreach (var router in Services.Routers)
        {
            try
            {
                var url = new Uri($"{router.Url}/route/v1/{router.Profile}/{coordinates}?overview=full&geometries=polyline6");
                var (status, body) = await GetAsync(url, cancellation).ConfigureAwait(false);
                if (status == HttpStatusCode.OK || IsNoRoute(body))
                {
                    _store.SetGeoCache(Kind, coordinates, body, _time.GetUtcNow());
                    return ToLeg(from, to, body);
                }

                Log.Warning($"Router {router.Url.Host} answered HTTP {(int)status}");
            }
            catch (MapServiceException ex)
            {
                Log.Warning($"Router {router.Url.Host} failed: {ex.Message}");
            }
        }

        return Straight(from, to) with { IsOffline = true };
    }

    /// <summary>A great-circle leg, used when there is no road route.</summary>
    public static RouteLeg Straight(Place from, Place to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var meters = Geo.Distance(from.Point, to.Point);
        var segments = (int)Math.Clamp(meters / 50_000, 8, 256);
        return new RouteLeg(from, to, meters, Geo.GreatCircle(from.Point, to.Point, segments), IsStraight: true);
    }

    public void Dispose() => _http.Dispose();

    private static RouteLeg ToLeg(Place from, Place to, string body)
    {
        try
        {
            return MapJson.ParseRoute(body) is { } route
                ? new RouteLeg(from, to, route.Meters, route.Line, IsStraight: false)
                : Straight(from, to);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            Log.Warning($"Unreadable route response: {ex.Message}");
            return Straight(from, to);
        }
    }

    private static bool IsNoRoute(string body) =>
        body.Contains("\"NoRoute\"", StringComparison.Ordinal) || body.Contains("\"NoSegment\"", StringComparison.Ordinal);

    private async Task<string> CachedAsync(string kind, string key, Func<Task<string>> fetch)
    {
        if (_store.GetGeoCache(kind, key) is { } cached)
        {
            return cached.Value;
        }

        var body = await fetch().ConfigureAwait(false);
        _store.SetGeoCache(kind, key, body, _time.GetUtcNow());
        return body;
    }

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(Uri url, CancellationToken cancellation)
    {
        await Gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var wait = WaitTime(LastRequest.TryGetValue(url.Host, out var last) ? last : null, _time.GetUtcNow());
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, cancellation).ConfigureAwait(false);
            }

            LastRequest[url.Host] = _time.GetUtcNow();
            Log.Info($"Map request: {url.Host}{url.AbsolutePath.Split("/route/")[0]}");
            using var response = await _http.GetAsync(url, cancellation).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return (response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            throw new MapServiceException(ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            throw new MapServiceException("Timed out", ex); // HttpClient timeout, not the caller cancelling
        }
        finally
        {
            Gate.Release();
        }
    }
}
