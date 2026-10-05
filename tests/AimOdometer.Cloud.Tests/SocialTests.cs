using System.Net;
using AimOdometer.Cloud;

namespace AimOdometer.Cloud.Tests;

public sealed class SocialClientTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private SessionStore SignedIn()
    {
        var store = new SessionStore(Path.Combine(_folder.Path, "s.dat"));
        store.Save(new CloudSession("access", "refresh", Now.AddHours(1), "user-1", "76561197960435530", "me", "", ""));
        return store;
    }

    [Fact]
    public async Task FriendsBoardIsRankedInOrder()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, """
            {"private":false,"friendsOnSteam":12,"rows":[
              {"name":"B","avatar":"https://a/b.jpg","centimeters":500,"isMe":false},
              {"name":"me","avatar":"","centimeters":300,"isMe":true}]}
            """));
        using var client = new CloudClient(SignedIn(), server, new FixedTime(Now));
        var board = await client.GetFriendsBoardAsync("week", "steam:730", TestContext.Current.CancellationToken);
        Assert.Equal("/functions/v1/social/friends?period=week&game=steam%3A730", server.Requests[0].Path);
        Assert.Equal(12, board.FriendsOnSteam);
        Assert.False(board.IsPrivate);
        Assert.Equal([new BoardRow(1, "B", "https://a/b.jpg", 500, false), new BoardRow(2, "me", "", 300, true)],
            board.Rows.Select(r => r with { Badges = null, Titles = null }));
        Assert.All(board.Rows, r => Assert.Empty(r.Badges!)); // an older server sends no DPI, badges or titles
        Assert.All(board.Rows, r => Assert.Empty(r.Titles!));
    }

    [Fact]
    public async Task BoardRowsCarryDpiAndShameBadges()
    {
        var server = new FakeServer((_, _) => (HttpStatusCode.OK, """
            {"private":false,"friendsOnSteam":1,"rows":[
              {"name":"C","avatar":"","centimeters":1200000,"isMe":false,"dpi":100,"peakSpeed":900,"peakDpi":100,"badges":["clown",7],"titles":["beta-tester"],"streak":123},
              {"name":"me","avatar":"","centimeters":300,"isMe":true,"dpi":null,"peakSpeed":0,"peakDpi":null,"badges":[]}]}
            """));
        using var client = new CloudClient(SignedIn(), server, new FixedTime(Now));
        var board = await client.GetFriendsBoardAsync("week", "*", TestContext.Current.CancellationToken);
        var c = board.Rows[0];
        Assert.Equal((100.0, 900.0, 100.0), (c.Dpi, c.PeakSpeed, c.PeakDpi));
        Assert.Equal(["clown"], c.Badges!);
        Assert.Equal(["beta-tester"], c.Titles!);
        Assert.Null(board.Rows[1].Dpi);
        Assert.Null(board.Rows[1].PeakDpi);
        Assert.Equal(123, c.Streak);
        Assert.Equal(0, board.Rows[1].Streak); // no streak sent: none shown
    }

    [Fact]
    public async Task WorldBoardWithAndWithoutMyPlace()
    {
        var withMe = new FakeServer((_, _) => (HttpStatusCode.OK, """
            {"participating":true,"players":1234,"rows":[{"rank":1,"name":"D","avatar":"","centimeters":900,"isMe":false}],
             "me":{"rank":567,"centimeters":12,"players":1234}}
            """));
        using (var client = new CloudClient(SignedIn(), withMe, new FixedTime(Now)))
        {
            var board = await client.GetWorldBoardAsync("all", "*", TestContext.Current.CancellationToken);
            Assert.Equal(new WorldPlace(567, 12, 1234), board.Me);
            Assert.Equal(1234, board.Players);
            Assert.Single(board.Rows);
        }

        var notRanked = new FakeServer((_, _) => (HttpStatusCode.OK, """{"participating":false,"players":0,"rows":[],"me":null}"""));
        using (var client = new CloudClient(SignedIn(), notRanked, new FixedTime(Now)))
        {
            var board = await client.GetWorldBoardAsync("week", "*", TestContext.Current.CancellationToken);
            Assert.Null(board.Me);
            Assert.False(board.Participating);
        }
    }

    [Fact]
    public async Task PrivacyIsReadAndWrittenOnTheOwnProfile()
    {
        var server = new FakeServer((r, _) => r.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, """[{"share_with_friends":true,"show_in_world":false}]""")
            : (HttpStatusCode.NoContent, string.Empty));
        using var client = new CloudClient(SignedIn(), server, new FixedTime(Now));
        Assert.Equal(new Privacy(true, false), await client.GetPrivacyAsync(TestContext.Current.CancellationToken));
        await client.SetPrivacyAsync(new Privacy(false, true), TestContext.Current.CancellationToken);
        var patch = server.Requests[1];
        Assert.Equal("/rest/v1/profiles?user_id=eq.user-1", patch.Path);
        Assert.Equal("""{"share_with_friends":false,"show_in_world":true}""", patch.Body);
        Assert.Equal("access", patch.Bearer);
    }
}
