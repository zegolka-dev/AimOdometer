using System.Collections.ObjectModel;
using System.Windows;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Cloud;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

public enum BoardKind
{
    Friends,
    World,
}

public enum BoardPeriod
{
    Week,
    Month,
    All,
}

/// <summary>A game to rank by; key "*" = all movement.</summary>
public sealed record BoardGame(string Key, string Name)
{
    public override string ToString() => Name;
}

/// <summary>What a tag next to a name means; decides its look (creator violet, beta tester sky blue, shame red).</summary>
public enum BoardTagKind
{
    Creator,
    BetaTester,
    Shame,
}

/// <summary>A tag next to a name: an honorary title or a shame badge for inflated distance (FairPlay).</summary>
public sealed record BoardTag(BoardTagKind Kind, string Name, string Description)
{
    public string Glyph => Kind switch
    {
        BoardTagKind.Creator => "\uE735",    // filled star
        BoardTagKind.BetaTester => "\uEBE8", // bug
        _ => "\uE7BA",                       // warning
    };
}

/// <summary>One leaderboard line as shown. <paramref name="Measured"/>: the DPI behind the distance and the fastest flick.</summary>
public sealed record BoardItem(string Rank, string Name, Uri? Avatar, string Distance, bool IsMe, string? Measured = null,
    IReadOnlyList<BoardTag>? Badges = null)
{
    public bool HasBadges => Badges is { Count: > 0 };
}

/// <summary>
/// Friends and world leaderboards (needs Steam sign-in). Friends: everyone in your Steam friend list who uses
/// AimOdometer and shares with friends. World: players who opted in; your own place even outside the top 100.
/// </summary>
public sealed partial class FriendsViewModel : PageViewModel
{
    private readonly CloudService _cloud;
    private int _generation;
    private bool _loadingGames;

    public FriendsViewModel(AppData data, CloudService cloud)
        : base(data)
    {
        _cloud = cloud;
        cloud.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CloudService.State) && IsCurrent)
            {
                Refresh();
            }
        };
    }

    public override string TitleKey => "Nav.Friends";

    public override string Icon => "";

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    public partial BoardKind Board { get; set; }

    [ObservableProperty]
    public partial BoardPeriod Period { get; set; }

    public ObservableCollection<BoardGame> Games { get; } = [];

    [ObservableProperty]
    public partial BoardGame? Game { get; set; }

    public ObservableCollection<BoardItem> Rows { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Explanation above the list (private friend list, nobody yet, not in the world board, errors).</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool CanJoinWorld { get; set; }

    [ObservableProperty]
    public partial bool CanInvite { get; set; }

    /// <summary>"Your place: 567 of 1 234" when it is not in the shown top.</summary>
    [ObservableProperty]
    public partial string? MyPlace { get; set; }

    private static Loc L => Loc.Instance;

    public override void Refresh()
    {
        IsSignedIn = _cloud.Client.IsSignedIn || DemoSocial.Boards is not null;
        if (!IsSignedIn)
        {
            Rows.Clear();
            Message = null;
            return;
        }

        LoadGames();
        _ = LoadAsync();
    }

    partial void OnBoardChanged(BoardKind value) => _ = LoadAsync();

    partial void OnPeriodChanged(BoardPeriod value) => _ = LoadAsync();

    partial void OnGameChanged(BoardGame? value)
    {
        if (!_loadingGames)
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        await _cloud.SignInAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task JoinWorldAsync()
    {
        try
        {
            var privacy = await _cloud.Client.GetPrivacyAsync(CancellationToken.None);
            await _cloud.Client.SetPrivacyAsync(privacy with { ShowInWorld = true }, CancellationToken.None);
            CanJoinWorld = false;
            Message = L["Friends.JoinedWorld"];
        }
        catch (CloudException ex)
        {
            Message = CloudService.Describe(ex.Error);
        }
    }

    [RelayCommand]
    private void CopyInvite()
    {
        Clipboard.SetText(L.Format("Friends.InviteText", $"{AppIdentity.Repository}/releases"));
        Message = L["Friends.InviteCopied"];
    }

    /// <summary>All movement plus the games played on this PC, most distance first.</summary>
    private void LoadGames()
    {
        var selected = Game?.Key ?? "*";
        _loadingGames = true;
        Games.Clear();
        Games.Add(new BoardGame("*", L["Friends.AllMovement"]));
        var totals = GameStats.Summarize(Data.Catalog, Data.Store.GetApps(), Data.Store.GetAppUsage(), L["Games.Other"]);
        foreach (var game in totals.Where(t => t.Category == AppCategory.Game && t.Centimeters > 0).OrderByDescending(t => t.Centimeters).Take(20))
        {
            Games.Add(new BoardGame(CloudSync.Limit(game.Key), game.Name));
        }

        Game = Games.FirstOrDefault(g => g.Key == selected) ?? Games[0];
        _loadingGames = false;
    }

    private async Task LoadAsync()
    {
        if ((!_cloud.Client.IsSignedIn && DemoSocial.Boards is null) || Game is null)
        {
            return;
        }

        var generation = ++_generation;
        var period = Period switch { BoardPeriod.Week => "week", BoardPeriod.Month => "month", _ => "all" };
        IsLoading = true;
        Message = null;
        MyPlace = null;
        CanJoinWorld = CanInvite = false;
        if (DemoSocial.Boards is { } demo)
        {
            ShowDemo(demo, period);
            IsLoading = false;
            return;
        }

        try
        {
            if (Board == BoardKind.Friends)
            {
                var board = await _cloud.Client.GetFriendsBoardAsync(period, Game.Key, CancellationToken.None);
                if (generation != _generation)
                {
                    return;
                }

                Show(board.Rows);
                if (board.IsPrivate)
                {
                    Message = L["Friends.PrivateList"];
                }
                else if (board.Rows.Count <= 1)
                {
                    Message = L["Friends.NobodyYet"];
                    CanInvite = true;
                }
            }
            else
            {
                var board = await _cloud.Client.GetWorldBoardAsync(period, Game.Key, CancellationToken.None);
                if (generation != _generation)
                {
                    return;
                }

                Show(board.Rows);
                if (!board.Participating)
                {
                    Message = L["Friends.WorldOptIn"];
                    CanJoinWorld = true;
                }
                else if (board.Me is { } me && board.Rows.All(r => !r.IsMe))
                {
                    MyPlace = L.Format("Friends.MyPlace", Format.Number(me.Rank), Format.Number(me.Players), Format.Distance(me.Centimeters));
                }
                else if (board.Me is null)
                {
                    Message = L["Friends.WorldNotYet"];
                }
            }
        }
        catch (CloudException ex)
        {
            Log.Warning($"Leaderboard failed: {ex.Error} {ex.Message}");
            if (generation == _generation)
            {
                Rows.Clear();
                Message = CloudService.Describe(ex.Error);
                IsSignedIn = _cloud.Client.IsSignedIn;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Promo recordings: canned boards (see <see cref="DemoSocial"/>), scaled by the period.</summary>
    private void ShowDemo(DemoBoards demo, string period)
    {
        var factor = DemoSocial.PeriodFactor(period);
        var rows = Board == BoardKind.Friends ? demo.Friends : demo.World;
        Show([.. rows.Select(r => r with { Centimeters = r.Centimeters * factor })]);
        if (Board == BoardKind.World && demo.Me is { } me && rows.All(r => !r.IsMe))
        {
            MyPlace = L.Format("Friends.MyPlace", Format.Number(me.Rank), Format.Number(me.Players), Format.Distance(me.Centimeters * factor));
        }
    }

    private static readonly HashSet<string> KnownBadges = new(StringComparer.Ordinal) { "clown", "blockhead", "fool", "booster" };

    /// <summary>Titles first (creator, then beta tester), then shame badges; unknown ids from a newer server are skipped.</summary>
    private static List<BoardTag> Tags(BoardRow row)
    {
        var titles = row.Titles ?? [];
        var tags = new List<BoardTag>();
        if (titles.Contains("creator"))
        {
            tags.Add(new BoardTag(BoardTagKind.Creator, L["Title.creator"], L["Title.creator.Desc"]));
        }

        if (titles.Contains("beta-tester"))
        {
            tags.Add(new BoardTag(BoardTagKind.BetaTester, L["Title.beta-tester"], L["Title.beta-tester.Desc"]));
        }

        tags.AddRange((row.Badges ?? []).Where(KnownBadges.Contains)
            .Select(b => new BoardTag(BoardTagKind.Shame, L[$"Badge.{b}"], L[$"Badge.{b}.Desc"])));
        return tags;
    }

    /// <summary>"800 DPI · flick 4.7 m/s at 1600 DPI": what the distance and the record were measured with.</summary>
    private static string? Measured(BoardRow row)
    {
        var parts = new List<string>(2);
        if (row.Dpi is > 0 and var dpi)
        {
            parts.Add(L.Format("Friends.DistanceDpi", Format.Number(Math.Round(dpi))));
        }

        if (row.PeakSpeed > 0)
        {
            parts.Add(row.PeakDpi is > 0 and var peakDpi
                ? L.Format("Friends.FlickAtDpi", Format.Speed(row.PeakSpeed), Format.Number(Math.Round(peakDpi)))
                : L.Format("Friends.Flick", Format.Speed(row.PeakSpeed)));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void Show(IReadOnlyList<BoardRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            var avatar = Uri.TryCreate(row.AvatarUrl, UriKind.Absolute, out var url)
                && (url.Scheme == Uri.UriSchemeHttps || (url.IsFile && DemoSocial.Boards is not null)) ? url : null;
            var name = row.Name.Length > 0 ? row.Name : L["Friends.NoName"];
            Rows.Add(new BoardItem(Format.Number(row.Rank), row.IsMe ? L.Format("Friends.Me", name) : name, avatar, Format.Distance(row.Centimeters), row.IsMe,
                Measured(row), Tags(row)));
        }
    }
}
