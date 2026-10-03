using System.Net;
using AimOdometer.Core.Map;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public class GeoTests
{
    private static readonly GeoPoint Chisinau = new(47.0105, 28.8638);
    private static readonly GeoPoint Odesa = new(46.4825, 30.7233);

    [Fact]
    public void Distance_MatchesKnownValue() =>
        Assert.InRange(Geo.Distance(Chisinau, Odesa), 153_000, 153_700); // ~153 km as the crow flies (flat-earth estimate: 153.3 km)

    [Fact]
    public void ToString_DoesNotRecurse() // WPF asks data items for ToString (accessibility)
    {
        Assert.Contains("Lat = 1", new GeoPoint(1, 2).ToString(), StringComparison.Ordinal);
        Assert.Contains("Odesa", new Place("Odesa", "Odesa, UA", new GeoPoint(1, 2)).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Distance_OneDegreeOfLatitude() =>
        Assert.Equal(111_195, Geo.Distance(new GeoPoint(0, 0), new GeoPoint(1, 0)), tolerance: 5);

    [Fact]
    public void PointAlong_InterpolatesByLength()
    {
        GeoPoint[] line = [new(0, 0), new(0, 1), new(0, 3)];
        Assert.Equal(new GeoPoint(0, 0), Geo.PointAlong(line, 0));
        Assert.Equal(1.5, Geo.PointAlong(line, 0.5).Lon, precision: 6);
        Assert.Equal(3, Geo.PointAlong(line, 1).Lon, precision: 6);
        Assert.Equal(3, Geo.PointAlong(line, 7).Lon, precision: 6); // clamped
    }

    [Fact]
    public void Cut_KeepsVerticesBeforeTheCut()
    {
        GeoPoint[] line = [new(0, 0), new(0, 1), new(0, 3)];
        var cut = Geo.Cut(line, 0.5);
        Assert.Equal(3, cut.Count);
        Assert.Equal(1, cut[1].Lon, precision: 9);
        Assert.Equal(1.5, cut[2].Lon, precision: 6);
    }

    [Fact]
    public void GreatCircle_EndsAtBothPointsAndKeepsLength()
    {
        var line = Geo.GreatCircle(Chisinau, Odesa, 16);
        Assert.Equal(17, line.Count);
        Assert.Equal(Chisinau, line[0]);
        Assert.Equal(Odesa.Lat, line[^1].Lat, precision: 6);
        Assert.Equal(Odesa.Lon, line[^1].Lon, precision: 6);
        Assert.Equal(Geo.Distance(Chisinau, Odesa), Geo.Length(line), tolerance: 1);
    }

    [Fact]
    public void GreatCircle_StaysContinuousAcrossTheAntimeridian()
    {
        var tokyo = new GeoPoint(35.68, 139.69);
        var losAngeles = new GeoPoint(34.05, -118.24);
        var line = Geo.GreatCircle(tokyo, losAngeles, 64);
        for (var i = 1; i < line.Count; i++)
        {
            Assert.True(Math.Abs(line[i].Lon - line[i - 1].Lon) < 10, $"jump at {i}");
        }

        Assert.Equal(-118.24, line[^1].Normalize().Lon, precision: 6);
    }
}

public class RoutePlanTests
{
    private static readonly Place A = new("A", "A", new GeoPoint(0, 0));
    private static readonly Place B = new("B", "B", new GeoPoint(0, 1));
    private static readonly Place C = new("C", "C", new GeoPoint(0, 2));

    // Road distances deliberately differ from the line lengths: positions are proportional within a leg.
    private static readonly RouteLeg[] Legs =
    [
        new(A, B, 100_000, [A.Point, B.Point], false),
        new(B, C, 300_000, [B.Point, C.Point], false),
    ];

    [Fact]
    public void Locate_WithinFirstLeg()
    {
        var p = RoutePlan.Locate(Legs, 50_000)!;
        Assert.Equal(0, p.LegIndex);
        Assert.Equal(0.5, p.Point.Lon, precision: 6);
        Assert.False(p.Finished);
    }

    [Fact]
    public void Locate_WithinSecondLeg()
    {
        var p = RoutePlan.Locate(Legs, 250_000)!;
        Assert.Equal(1, p.LegIndex);
        Assert.Equal(0.5, p.Fraction, precision: 9);
        Assert.Equal(1.5, p.Point.Lon, precision: 6);
    }

    [Fact]
    public void Locate_BeyondTheEnd()
    {
        var p = RoutePlan.Locate(Legs, 450_000)!;
        Assert.True(p.Finished);
        Assert.Equal(50_000, p.ExtraMeters, precision: 6);
        Assert.Equal(C.Point, p.Point);
        Assert.Equal(400_000, RoutePlan.TotalMeters(Legs));
    }

    [Fact]
    public void Locate_NoLegs() => Assert.Null(RoutePlan.Locate([], 10));

    [Fact]
    public void Walked_ConcatenatesFinishedLegs()
    {
        var walked = RoutePlan.Walked(Legs, 250_000);
        Assert.Equal(1.5, walked[^1].Lon, precision: 6);
        Assert.Equal(A.Point, walked[0]);
    }
}

public class MapJsonTests
{
    [Fact]
    public void ParseSearch_ReadsNominatim()
    {
        const string json = """
            [{"place_id":1,"lat":"46.4843","lon":"30.7326","name":"Одесса","display_name":"Одесса, Одесская область, Украина"},
             {"place_id":2,"lat":"1.5","lon":"2.5","name":"","display_name":"Somewhere, Region"}]
            """;
        var places = MapJson.ParseSearch(json);
        Assert.Equal("Одесса", places[0].Name);
        Assert.Equal(46.4843, places[0].Point.Lat);
        Assert.Equal("Somewhere", places[1].Name); // falls back to the first part of the display name
    }

    [Fact]
    public void ParseReverse_PrefersCity()
    {
        Assert.Equal("Тирасполь", MapJson.ParseReverse("""{"name":"x","address":{"state":"S","city":"Тирасполь"}}"""));
        Assert.Equal("Village", MapJson.ParseReverse("""{"address":{"village":"Village","county":"C"}}"""));
        Assert.Null(MapJson.ParseReverse("""{"error":"Unable to geocode"}"""));
    }

    [Fact]
    public void ParseRoute_ReadsOsrm()
    {
        const string json = """
            {"code":"Ok","routes":[{"distance":232037.3,"geometry":{"type":"LineString","coordinates":[[28.86,47.01],[30.72,46.48]]}}]}
            """;
        var route = MapJson.ParseRoute(json)!.Value;
        Assert.Equal(232037.3, route.Meters);
        Assert.Equal(new GeoPoint(47.01, 28.86), route.Line[0]);
    }

    [Fact]
    public void ParseRoute_ReadsPolyline6()
    {
        // 38.5,-120.2 → 40.7,-120.95 → 43.252,-126.453 (the reference example, at precision 6).
        const string json = """{"code":"Ok","routes":[{"distance":1000,"geometry":"_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI"}]}""";
        var line = MapJson.ParseRoute(json)!.Value.Line;
        Assert.Equal(3, line.Count);
        Assert.Equal(38.5, line[0].Lat, precision: 6);
        Assert.Equal(-120.95, line[1].Lon, precision: 6);
        Assert.Equal(43.252, line[2].Lat, precision: 6);
    }

    [Fact]
    public void DecodePolyline_ReferenceExample()
    {
        var line = MapJson.DecodePolyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@", 5);
        Assert.Equal(new GeoPoint(38.5, -120.2), line[0]);
        Assert.Equal(-126.453, line[2].Lon, precision: 9);
        Assert.Throws<FormatException>(() => MapJson.DecodePolyline("_p~iF~ps|U_", 5));
    }

    [Fact]
    public void ParseRoute_NoRouteIsNull() => Assert.Null(MapJson.ParseRoute("""{"code":"NoRoute","message":"Impossible route"}"""));

    [Fact]
    public void BuiltInServices_AreValid()
    {
        var services = ServicesConfig.BuiltIn;
        Assert.Equal("tiles.openfreemap.org", services.MapStyle.Host);
        Assert.NotEmpty(services.Routers);
        Assert.Contains("OSRM", services.Attribution, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"version":1,"mapStyle":"http://x.org/s","geocoder":"https://g.org","routers":[{"url":"https://r.org","profile":"driving"}]}""")]
    [InlineData("""{"version":2,"mapStyle":"https://x.org/s","geocoder":"https://g.org","routers":[{"url":"https://r.org","profile":"driving"}]}""")]
    [InlineData("""{"version":1,"mapStyle":"https://x.org/s","geocoder":"https://g.org","routers":[]}""")]
    [InlineData("""{"version":1,"mapStyle":"https://x.org/s","geocoder":"https://g.org","routers":[{"url":"https://r.org","profile":"../x"}]}""")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Services_RejectsUnsafeOrBroken(string json) => Assert.Null(ServicesConfig.Parse(json));

    [Fact]
    public void PlaceList_RoundTrips()
    {
        Place[] places = [new("Кишинёв", "Кишинёв, Молдова", new GeoPoint(47.01, 28.86)), new("Odesa", "Odesa, Ukraine", new GeoPoint(46.48, 30.72))];
        Assert.Equal(places, PlaceList.Read(PlaceList.Write(places)));
        Assert.Empty(PlaceList.Read("broken"));
        Assert.Empty(PlaceList.Read(null));
    }
}

public sealed class MapClientTests : IDisposable
{
    private static readonly Place From = new("A", "A", new GeoPoint(47.01, 28.86));
    private static readonly Place To = new("B", "B", new GeoPoint(46.48, 30.72));

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aimodometer-map-{Guid.NewGuid():N}.db");
    private readonly StatsStore _store;

    public MapClientTests() => _store = StatsStore.Open(_path);

    public void Dispose()
    {
        _store.Dispose();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(f);
        }
    }

    [Fact]
    public void WaitTime_KeepsOneSecondBetweenRequests()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        Assert.Equal(TimeSpan.Zero, MapClient.WaitTime(null, now));
        Assert.Equal(TimeSpan.FromMilliseconds(800), MapClient.WaitTime(now.AddMilliseconds(-300), now));
        Assert.Equal(TimeSpan.Zero, MapClient.WaitTime(now.AddSeconds(-5), now));
    }

    [Fact]
    public void UserAgent_IdentifiesTheApp() =>
        Assert.Matches(@"^AimOdometer/\d+\.\d+\.\d+ \(\+https://github\.com/zegolka-dev/AimOdometer\)$", MapClient.UserAgent);

    [Fact]
    public async Task Search_IsCachedAndSendsUserAgent()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, """[{"lat":"1","lon":"2","name":"Odesa","display_name":"Odesa, UA"}]"""));
        using var client = new MapClient(_store, handler);
        var first = await client.SearchAsync("  Odesa ", "en", TestContext.Current.CancellationToken);
        var second = await client.SearchAsync("odesa", "en", TestContext.Current.CancellationToken);
        Assert.Equal("Odesa", Assert.Single(first).Name);
        Assert.Equal(first, second);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("q=Odesa", request.Url.Query, StringComparison.Ordinal);
        Assert.Contains("accept-language=en", request.Url.Query, StringComparison.Ordinal);
        Assert.StartsWith("AimOdometer/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_ErrorIsNotCached()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? (HttpStatusCode.ServiceUnavailable, "") : (HttpStatusCode.OK, "[]"));
        using var client = new MapClient(_store, handler);
        await Assert.ThrowsAsync<MapServiceException>(() => client.SearchAsync("x", "en", TestContext.Current.CancellationToken));
        Assert.Empty(await client.SearchAsync("x", "en", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Route_FallsOverToTheNextRouterAndCaches()
    {
        var handler = new FakeHandler(url => url.Host == "routing.openstreetmap.de"
            ? (HttpStatusCode.BadGateway, "")
            : (HttpStatusCode.OK, """{"code":"Ok","routes":[{"distance":200000,"geometry":{"coordinates":[[28.86,47.01],[30.72,46.48]]}}]}"""));
        using var client = new MapClient(_store, handler);
        var leg = await client.RouteAsync(From, To, TestContext.Current.CancellationToken);
        Assert.False(leg.IsStraight);
        Assert.Equal(200_000, leg.Meters);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/route/v1/driving/28.86,47.01;30.72,46.48", handler.Requests[0].Url.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("geometries=polyline6", handler.Requests[0].Url.Query, StringComparison.Ordinal);

        await client.RouteAsync(From, To, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Requests.Count); // cached
    }

    [Fact]
    public async Task Route_NoRouteBecomesARememberedStraightLine()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.BadRequest, """{"code":"NoRoute","message":"Impossible route"}"""));
        using var client = new MapClient(_store, handler);
        var leg = await client.RouteAsync(From, To, TestContext.Current.CancellationToken);
        Assert.True(leg.IsStraight);
        Assert.False(leg.IsOffline);
        Assert.Equal(Geo.Distance(From.Point, To.Point), leg.Meters, precision: 6);
        await client.RouteAsync(From, To, TestContext.Current.CancellationToken);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Route_OfflineIsStraightButRetriedLater()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var client = new MapClient(_store, handler);
        var leg = await client.RouteAsync(From, To, TestContext.Current.CancellationToken);
        Assert.True(leg.IsStraight);
        Assert.True(leg.IsOffline);
        Assert.Null(_store.GetGeoCache("route-full", "28.86,47.01;30.72,46.48"));
    }

    [Fact]
    public async Task Services_UsesValidRemoteCopyAndFallsBackOtherwise()
    {
        const string remote = """{"version":1,"mapStyle":"https://tiles.example.org/dark","geocoder":"https://geo.example.org","routers":[{"url":"https://r.example.org","profile":"foot"}]}""";
        using (var client = new MapClient(_store, new FakeHandler(_ => (HttpStatusCode.OK, remote))))
        {
            var services = await client.LoadServicesAsync(TestContext.Current.CancellationToken);
            Assert.Equal("tiles.example.org", services.MapStyle.Host);
        }

        _store.SetGeoCache("services", "remote", "{}", DateTimeOffset.UtcNow.AddDays(-2)); // stale, and the site is down
        using (var client = new MapClient(_store, new FakeHandler(_ => (HttpStatusCode.NotFound, ""))))
        {
            var services = await client.LoadServicesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ServicesConfig.BuiltIn, services);
        }
    }

    private sealed class FakeHandler(Func<Uri, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<(Uri Url, string UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString()));
            var (status, body) = respond(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

public class FamousCitiesTests
{
    private static readonly GeoPoint Chisinau = new(47.0105, 28.8638);

    [Fact]
    public void List_IsLargeAndUnique()
    {
        Assert.True(FamousCities.All.Count >= 80);
        Assert.Equal(FamousCities.All.Count, FamousCities.All.Select(c => c.Id).Distinct().Count());
        Assert.NotNull(FamousCities.Find("paris"));
        Assert.Null(FamousCities.Find("atlantis"));
    }

    [Fact]
    public void Pick_IsAJourneyButNotTooFar()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var city = FamousCities.Pick(Chisinau, 0, new Random(seed));
            Assert.InRange(Geo.Distance(Chisinau, city.Point), FamousCities.MinimumMeters, 2_500_000);
        }
    }

    [Fact]
    public void Pick_IsNotReachedYet()
    {
        var city = FamousCities.Pick(Chisinau, 1_500_000, new Random(1));
        Assert.True(Geo.Distance(Chisinau, city.Point) >= 1_650_000);
    }

    [Fact]
    public void Pick_SkipsTheCurrentOne()
    {
        var first = FamousCities.Pick(Chisinau, 0, new Random(3));
        for (var seed = 0; seed < 30; seed++)
        {
            Assert.NotEqual(first.Id, FamousCities.Pick(Chisinau, 0, new Random(seed), except: [first.Id]).Id);
        }
    }

    [Fact]
    public void Pick_FallsBackForHugeWalks() =>
        Assert.NotNull(FamousCities.Pick(Chisinau, 30_000_000, new Random(1))); // longer than any city is far
}
