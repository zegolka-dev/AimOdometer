using System.Net;
using System.Text.Json;
using AimOdometer.Cloud;
using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Cloud.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Answers requests by path; records what was sent.</summary>
internal sealed class FakeServer(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
{
    public List<(string Path, string Body, string? Bearer, string? ApiKey)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.Parameter,
            request.Headers.TryGetValues("apikey", out var keys) ? keys.Single() : null));
        var (status, text) = respond(request, body);
        return new HttpResponseMessage(status) { Content = new StringContent(text) };
    }
}

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aimodometer-cloud-{Guid.NewGuid():N}");

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A database file still closing; the temp folder is cleaned by Windows eventually.
        }
    }
}

public sealed class SessionStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void RoundTripsEncrypted()
    {
        var store = new SessionStore(Path.Combine(_folder.Path, "cloud", "session.dat"));
        Assert.Null(store.Load());
        var session = new CloudSession("access", "refresh-secret", DateTimeOffset.FromUnixTimeSeconds(2_000_000_000), "user", "76561197960435530", "Имя", "https://a/b.jpg", "https://p");
        store.Save(session);
        Assert.Equal(session, store.Load());
        var bytes = File.ReadAllBytes(store.Path);
        Assert.DoesNotContain("refresh-secret", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal); // DPAPI, not plain text
        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void DamagedFileMeansSignedOut()
    {
        var store = new SessionStore(Path.Combine(_folder.Path, "session.dat"));
        File.WriteAllBytes(store.Path, [1, 2, 3]);
        Assert.Null(store.Load());
    }
}

public class LoopbackTests
{
    [Fact]
    public async Task AnswersTheCallbackAndIgnoresOtherPaths()
    {
        using var loopback = new LoopbackCallback();
        var wait = loopback.WaitAsync("/callback", q => $"<p>{q["code"]}</p>", TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var favicon = await http.GetAsync(new Uri($"http://127.0.0.1:{loopback.Port}/favicon.ico"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        var page = await http.GetStringAsync(new Uri($"http://127.0.0.1:{loopback.Port}/callback?code=abc&state=x%20y"), TestContext.Current.CancellationToken);
        Assert.Equal("<p>abc</p>", page);
        var query = await wait;
        Assert.Equal("x y", query["state"]);
    }

    [Fact]
    public void ParsesQueries()
    {
        var q = LoopbackCallback.ParseQuery("a=1&b=%D0%BF&c&a=2");
        Assert.Equal("1", q["a"]);
        Assert.Equal("п", q["b"]);
        Assert.Equal(string.Empty, q["c"]);
    }
}

public sealed class SteamSignInTests : IDisposable
{
    private static readonly SignInPages Pages = new("OK", "Done <now>", "Oops", "Failed");
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ChallengeMatchesThePkceReference() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", SteamSignIn.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void PagesAreHtmlEncoded() => Assert.Contains("Done &lt;now&gt;", SteamSignIn.Page(("OK", "Done <now>")), StringComparison.Ordinal);

    [Fact]
    public async Task FullFlowExchangesTheCodeWithTheVerifier()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, """
            {"access_token":"at","refresh_token":"rt","expires_at":2000000000,"user_id":"u1",
             "profile":{"steam_id":"76561197960435530","persona_name":"zegolka","avatar_url":"https://a","profile_url":"https://p"}}
            """));
        using var client = new CloudClient(new SessionStore(Path.Combine(_folder.Path, "s.dat")), server);
        string? challenge = null;
        string? html = null;
        var session = await SteamSignIn.SignInAsync(client, start =>
        {
            var q = LoopbackCallback.ParseQuery(start.Query.TrimStart('?'));
            challenge = q["challenge"];
            Assert.Equal("/functions/v1/auth-steam/start", start.AbsolutePath);
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                html = await http.GetStringAsync(new Uri($"http://127.0.0.1:{q["port"]}/callback?code=thecode&state={q["state"]}"));
            });
            return true;
        }, Pages, TestContext.Current.CancellationToken);

        Assert.Equal("zegolka", session.PersonaName);
        Assert.True(client.IsSignedIn);
        var exchange = Assert.Single(server.Requests);
        Assert.Equal("/functions/v1/auth-steam/exchange", exchange.Path);
        using var body = JsonDocument.Parse(exchange.Body);
        Assert.Equal("thecode", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(challenge, SteamSignIn.Challenge(body.RootElement.GetProperty("verifier").GetString()!));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Contains("Done", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error=cancelled", CloudError.SignInCancelled)]
    [InlineData("error=invalid", CloudError.SignInDenied)]
    [InlineData("code=x&wrongstate=1", CloudError.SignInDenied)]
    public async Task FailuresAreReported(string answer, CloudError expected)
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, "{}"));
        using var client = new CloudClient(new SessionStore(Path.Combine(_folder.Path, "s.dat")), server);
        var ex = await Assert.ThrowsAsync<CloudException>(() => SteamSignIn.SignInAsync(client, start =>
        {
            var q = LoopbackCallback.ParseQuery(start.Query.TrimStart('?'));
            var state = answer.Contains("wrongstate", StringComparison.Ordinal) ? "someone-else" : q["state"];
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                await http.GetStringAsync(new Uri($"http://127.0.0.1:{q["port"]}/callback?{answer}&state={state}"));
            });
            return true;
        }, Pages, TestContext.Current.CancellationToken));
        Assert.Equal(expected, ex.Error);
        Assert.Empty(server.Requests); // nothing was exchanged
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task NoBrowserNoSignIn()
    {
        using var client = new CloudClient(new SessionStore(Path.Combine(_folder.Path, "s.dat")), new FakeServer((_, _) => (HttpStatusCode.OK, "{}")));
        var ex = await Assert.ThrowsAsync<CloudException>(() => SteamSignIn.SignInAsync(client, _ => false, Pages, TestContext.Current.CancellationToken));
        Assert.Equal(CloudError.BrowserFailed, ex.Error);
    }
}

public sealed class CloudClientTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private SessionStore SignedIn(DateTimeOffset expires)
    {
        var store = new SessionStore(Path.Combine(_folder.Path, "s.dat"));
        store.Save(new CloudSession("old-access", "old-refresh", expires, "u1", "76561197960435530", "z", "", ""));
        return store;
    }

    [Fact]
    public async Task UsesTheTokenAndThePublishableKey()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, """{"accepted":3,"rejected":1}"""));
        using var client = new CloudClient(SignedIn(Now.AddHours(1)), server, new FixedTime(Now));
        var result = await client.SyncAsync("{}", TestContext.Current.CancellationToken);
        Assert.Equal(new SyncResponse(3, 1), result);
        var request = Assert.Single(server.Requests);
        Assert.Equal("old-access", request.Bearer);
        Assert.StartsWith("sb_publishable_", request.ApiKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshesShortlyBeforeExpiry()
    {
        var server = new FakeServer((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":3600}""")
            : (HttpStatusCode.OK, """{"accepted":0,"rejected":0}"""));
        var store = SignedIn(Now.AddSeconds(30));
        using var client = new CloudClient(store, server, new FixedTime(Now));
        await client.SyncAsync("{}", TestContext.Current.CancellationToken);
        Assert.Equal("/auth/v1/token?grant_type=refresh_token", server.Requests[0].Path);
        Assert.Contains("old-refresh", server.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal("new-access", server.Requests[1].Bearer);
        Assert.Equal("new-refresh", store.Load()!.RefreshToken); // rotated token persisted
    }

    [Fact]
    public async Task RevokedRefreshTokenSignsOut()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var store = SignedIn(Now.AddSeconds(-5));
        using var client = new CloudClient(store, server, new FixedTime(Now));
        var ex = await Assert.ThrowsAsync<CloudException>(() => client.SyncAsync("{}", TestContext.Current.CancellationToken));
        Assert.Equal(CloudError.SessionExpired, ex.Error);
        Assert.False(client.IsSignedIn);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task ServerTroubleDuringRefreshKeepsTheSession()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.BadGateway, ""));
        using var client = new CloudClient(SignedIn(Now.AddSeconds(-5)), server, new FixedTime(Now));
        var ex = await Assert.ThrowsAsync<CloudException>(() => client.SyncAsync("{}", TestContext.Current.CancellationToken));
        Assert.Equal(CloudError.ServerError, ex.Error);
        Assert.True(client.IsSignedIn);
    }

    [Fact]
    public async Task OfflineKeepsTheSession()
    {
        var server = new FakeServer((_, _) => throw new HttpRequestException("no network"));
        using var client = new CloudClient(SignedIn(Now.AddHours(1)), server, new FixedTime(Now));
        var ex = await Assert.ThrowsAsync<CloudException>(() => client.SyncAsync("{}", TestContext.Current.CancellationToken));
        Assert.Equal(CloudError.Offline, ex.Error);
        Assert.True(client.IsSignedIn);
    }

    [Fact]
    public async Task UnauthorizedSignsOut()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.Unauthorized, """{"error":"Sign in first."}"""));
        using var client = new CloudClient(SignedIn(Now.AddHours(1)), server, new FixedTime(Now));
        await Assert.ThrowsAsync<CloudException>(() => client.SyncAsync("{}", TestContext.Current.CancellationToken));
        Assert.False(client.IsSignedIn);
    }

    [Fact]
    public async Task ReadsTotalsAndDevices()
    {
        var server = new FakeServer((r, _) => r.RequestUri!.AbsolutePath.EndsWith("my_devices", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """[{"id":"d","pc_id":"6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b","name":"PC","last_sync_at":"2026-10-04T10:00:00+00:00","centimeters":1500.5}]""")
            : (HttpStatusCode.OK, """[{"day":"2026-10-03","centimeters":12000,"clicks":30,"move_seconds":600}]"""));
        using var client = new CloudClient(SignedIn(Now.AddHours(1)), server, new FixedTime(Now));
        var days = await client.GetDailyTotalsAsync(new DateOnly(2026, 1, 1), TestContext.Current.CancellationToken);
        Assert.Equal(new CloudDayTotal(new DateOnly(2026, 10, 3), 12000, 30, 600), Assert.Single(days));
        Assert.Contains("2026-01-01", server.Requests[0].Body, StringComparison.Ordinal);
        var devices = await client.GetDevicesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1500.5, Assert.Single(devices).Centimeters);
    }

    [Fact]
    public async Task DeleteAccountForgetsTheSession()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, """{"deleted":true}"""));
        var store = SignedIn(Now.AddHours(1));
        using var client = new CloudClient(store, server, new FixedTime(Now));
        await client.DeleteAccountAsync(TestContext.Current.CancellationToken);
        Assert.Equal("/functions/v1/account-delete", server.Requests[0].Path);
        Assert.False(client.IsSignedIn);
        Assert.Null(store.Load());
    }
}

public sealed class CloudSyncTests : IDisposable
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);
    private static readonly DateOnly Day2 = new(2026, 10, 2);
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void AggregatesByDayGameAndMouse()
    {
        DailyBreakdown[] breakdown =
        [
            new(Day1, AppId: 1, DeviceId: 10, 100, 5, 60, 200),
            new(Day1, AppId: 2, DeviceId: 10, 50, 1, 30, 300),   // another non-game app: same "" key
            new(Day1, AppId: 3, DeviceId: 10, 70, 2, 20, 900),   // a game
            new(Day2, AppId: 1, DeviceId: 11, 0, 0, 0, 0),       // nothing happened: dropped
        ];
        var rows = CloudSync.Aggregate(breakdown, app => app == 3 ? "steam:730" : string.Empty, device => $"m{device}");
        Assert.Equal(2, rows.Count);
        var other = rows.Single(r => r.GameKey == string.Empty);
        Assert.Equal((150.0, 6L, 90L, 300.0), (other.Centimeters, other.Clicks, other.MoveSeconds, other.PeakSpeed));
        Assert.Equal("m10", other.MouseKey);
    }

    [Fact]
    public void AggregatedRowsCarryWeightedDpiAndTheDpiOfTheFastestFlick()
    {
        DailyBreakdown[] breakdown =
        [
            new(Day1, AppId: 1, DeviceId: 10, 100, 0, 60, 200, Dpi: 800, PeakDpi: 800),
            new(Day1, AppId: 2, DeviceId: 10, 300, 0, 60, 500, Dpi: 1600, PeakDpi: 3200),
        ];
        var row = Assert.Single(CloudSync.Aggregate(breakdown, _ => string.Empty, _ => "m"));
        Assert.Equal((1400.0, 3200.0), (row.Dpi, row.PeakDpi));
    }

    [Fact]
    public void BatchesNeverSplitADay()
    {
        var rows = Enumerable.Range(0, 7).Select(i => new CloudRow(Day1.AddDays(i / 3), $"g{i}", "", 1, 0, 0, 0)).ToList();
        var batches = CloudSync.Batches(rows, 4).ToList();
        Assert.Equal([3, 4], batches.Select(b => b.Count)); // days of 3, 3 and 1 rows, at most 4 per request
        var days = batches.SelectMany(b => b.Select(r => r.Day).Distinct()).ToList();
        Assert.Equal(days.Count, days.Distinct().Count()); // no day in two requests
    }

    [Fact]
    public void KeysAreSafeAndAnonymous()
    {
        Assert.Equal("exe:my game _x64_.exe", CloudSync.Limit("exe:my game (x64).exe"));
        Assert.Equal("exe:a_b.exe", CloudSync.Limit("exe:a&b.exe"));
        Assert.Equal(64, CloudSync.Limit(new string('a', 100)).Length);
        var key = CloudSync.MouseKey(@"\\?\HID#VID_046D&PID_C547#serial");
        Assert.Matches("^[0-9a-f]{16}$", key);
        Assert.Equal(key, CloudSync.MouseKey(@"\\?\HID#VID_046D&PID_C547#serial"));
    }

    [Fact]
    public void PayloadMatchesTheServerContract()
    {
        var pc = Guid.Parse("6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b");
        using var doc = JsonDocument.Parse(CloudSync.Payload(pc, "PC", [new CloudRow(Day1, "steam:730", "abc", 12.5, 3, 60, 250)]));
        var root = doc.RootElement;
        Assert.Equal(pc.ToString(), root.GetProperty("pcId").GetString());
        var row = root.GetProperty("rows")[0];
        Assert.Equal("2026-10-01", row.GetProperty("day").GetString());
        Assert.Equal(12.5, row.GetProperty("centimeters").GetDouble());
        Assert.Equal(60, row.GetProperty("moveSeconds").GetInt32());

        using var withDpi = JsonDocument.Parse(CloudSync.Payload(pc, "PC", [new CloudRow(Day1, "", "", 1, 0, 0, 0, 800, 1600)]));
        var dpiRow = withDpi.RootElement.GetProperty("rows")[0];
        Assert.Equal((800.0, 1600.0), (dpiRow.GetProperty("dpi").GetDouble(), dpiRow.GetProperty("peakDpi").GetDouble()));
    }

    [Fact]
    public async Task FirstSyncUploadsEverythingThenOnlyRecentDays()
    {
        var dbPath = Path.Combine(_folder.Path, "stats.db");
        using var store = StatsStore.Open(dbPath);
        var path = DevicePath.Parse(@"\\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var device = store.UpsertDevice(path.StableKey, path, DeviceKind.Mouse, null, DateTime.UtcNow);
        WriteDay(store, device.Id, new DateOnly(2026, 1, 15));
        WriteDay(store, device.Id, new DateOnly(2026, 10, 3));

        var sent = new List<string>();
        var server = new FakeServer((_, body) =>
        {
            sent.Add(body);
            return (HttpStatusCode.OK, """{"accepted":1,"rejected":0}""");
        });
        var time = new FixedTime(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var sessionStore = new SessionStore(Path.Combine(_folder.Path, "s.dat"));
        sessionStore.Save(new CloudSession("a", "r", time.Now.AddHours(1), "user-1", "76561197960435530", "z", "", ""));
        using var client = new CloudClient(sessionStore, server, time);
        var catalog = GameCatalog.Create(store, Path.Combine(_folder.Path, "games.json"));

        await CloudSync.RunAsync(client, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.Contains("2026-01-15", sent[^1], StringComparison.Ordinal); // full history the first time
        Assert.Equal(time.Now, CloudSync.LastSync(store));

        await CloudSync.RunAsync(client, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("2026-01-15", sent[^1], StringComparison.Ordinal);
        Assert.Contains("2026-10-03", sent[^1], StringComparison.Ordinal);

        CloudSync.RequestFullUpload(store); // e.g. after a DPI correction
        await CloudSync.RunAsync(client, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.Contains("2026-01-15", sent[^1], StringComparison.Ordinal);

        // History uploaded before rows carried DPI is sent again once.
        store.SetSetting(SettingKeys.CloudRowFormat, "1");
        await CloudSync.RunAsync(client, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.Contains("2026-01-15", sent[^1], StringComparison.Ordinal);
        Assert.Contains("\"dpi\":800", sent[^1], StringComparison.Ordinal);
        await CloudSync.RunAsync(client, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("2026-01-15", sent[^1], StringComparison.Ordinal);

        // Another account on this PC gets the full history too.
        sessionStore.Save(new CloudSession("a", "r", time.Now.AddHours(1), "user-2", "76561197960435531", "y", "", ""));
        using var other = new CloudClient(sessionStore, server, time);
        await CloudSync.RunAsync(other, store, catalog, time, TestContext.Current.CancellationToken);
        Assert.Contains("2026-01-15", sent[^1], StringComparison.Ordinal);

        // The PC id is stable.
        using var pcIds = JsonDocument.Parse(sent[0]);
        using var last = JsonDocument.Parse(sent[^1]);
        Assert.Equal(pcIds.RootElement.GetProperty("pcId").GetString(), last.RootElement.GetProperty("pcId").GetString());
    }

    private static void WriteDay(StatsStore store, long deviceId, DateOnly day)
    {
        store.WriteHour(new HourKey(day, 12, 180),
            [new UsageDelta(0, deviceId, 0, 800, new UsageBucket { PathCounts = 80_000, ClicksLeft = 5, MoveSeconds = 60, Dirty = true })]);
    }
}
