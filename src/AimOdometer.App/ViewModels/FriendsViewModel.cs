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

/// <summary>One leaderboard line as shown.</summary>
public sealed record BoardItem(string Rank, string Name, Uri? Avatar, string Distance, bool IsMe);

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
        IsSignedIn = _cloud.Client.IsSignedIn;
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
        if (!_cloud.Client.IsSignedIn || Game is null)
        {
            return;
        }

        var generation = ++_generation;
        var period = Period switch { BoardPeriod.Week => "week", BoardPeriod.Month => "month", _ => "all" };
        IsLoading = true;
        Message = null;
        MyPlace = null;
        CanJoinWorld = CanInvite = false;
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

    private void Show(IReadOnlyList<BoardRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            var avatar = Uri.TryCreate(row.AvatarUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;
            var name = row.Name.Length > 0 ? row.Name : L["Friends.NoName"];
            Rows.Add(new BoardItem(Format.Number(row.Rank), row.IsMe ? L.Format("Friends.Me", name) : name, avatar, Format.Distance(row.Centimeters), row.IsMe));
        }
    }
}
