using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.App.Views;
using AimOdometer.Core;
using AimOdometer.Core.Games;
using AimOdometer.Core.Stats;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

public enum ShareKind
{
    Day,
    Week,
    Month,
    AllTime,
    TopGames,
    Achievement,
}

/// <summary>A row on the card (top games).</summary>
public sealed record ShareRow(string Name, string Value, double Share, Brush Brush);

/// <summary>Everything the card shows.</summary>
public sealed record ShareCardData(
    string Title,
    string Subtitle,
    string Number,
    string Unit,
    string Caption,
    IReadOnlyList<ShareRow> Rows,
    string? Glyph,
    bool IsStory,
    string Footer);

/// <summary>Share overlay: pick a card and a size, then copy or save a PNG.</summary>
public sealed partial class ShareViewModel : ObservableObject
{
    public const int Width = 1080;
    public const int SquareHeight = 1080;
    public const int StoryHeight = 1920;

    private readonly AppData _data;
    private readonly Action _close;

    public ShareViewModel(AppData data, ShareKind kind, Action close)
    {
        _data = data;
        _close = close;
        Kinds = [.. Enum.GetValues<ShareKind>().Select(k => new ShareKindOption(k))];
        Kind = Kinds.First(k => k.Kind == kind);
        Render();
    }

    public IReadOnlyList<ShareKindOption> Kinds { get; }

    [ObservableProperty]
    public partial ShareKindOption Kind { get; set; }

    [ObservableProperty]
    public partial bool IsStory { get; set; }

    [ObservableProperty]
    public partial BitmapSource? Image { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>Pictures\AimOdometer; inside the data folder when it is overridden, so test runs leave Pictures alone.</summary>
    public static string Folder => AppIdentity.IsDataDirectoryOverridden
        ? Path.Combine(AppIdentity.DataDirectory, "share")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppIdentity.ProductName);

    partial void OnKindChanged(ShareKindOption value) => Render();

    partial void OnIsStoryChanged(bool value) => Render();

    [RelayCommand]
    private void Copy()
    {
        if (Image is { } image)
        {
            Clipboard.SetImage(image);
            Message = Loc.Instance["Share.Copied"];
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (Image is not { } image)
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        var name = string.Create(CultureInfo.InvariantCulture,
            $"aimodometer-{Kind.Kind.ToString().ToLowerInvariant()}-{(IsStory ? "story" : "square")}-{DateTime.Now:yyyy-MM-dd-HHmmss}.png");
        var path = Path.Combine(Folder, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        Message = Loc.Instance.Format("Share.Saved", path);
    }

    [RelayCommand]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = false })?.Dispose();
    }

    [RelayCommand]
    private void Close() => _close();

    private void Render()
    {
        Message = null;
        Image = ShareRenderer.Render(BuildCard(), Width, IsStory ? StoryHeight : SquareHeight);
    }

    /// <summary>Card content for the chosen kind from the current statistics.</summary>
    public ShareCardData BuildCard()
    {
        var L = Loc.Instance;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var days = _data.Store.GetDailyTotals();
        var periods = StatsSummary.Periods(days, today, firstDay);
        var footer = L.Format("Share.Footer", AppIdentity.Website.Replace("https://", string.Empty, StringComparison.Ordinal));

        ShareCardData Distance(string titleKey, string subtitle, double cm)
        {
            var (number, unit) = Format.DistanceParts(cm);
            return new ShareCardData(L[titleKey], subtitle, number, unit, OverviewViewModel.ComparisonText(cm), [], null, IsStory, footer);
        }

        switch (Kind.Kind)
        {
            case ShareKind.Day:
                return Distance("Share.Card.Day", Format.Date(today), periods.Today.Centimeters);
            case ShareKind.Week:
                var weekStart = StatsSummary.StartOfWeek(today, firstDay);
                return Distance("Share.Card.Week", $"{Format.ShortDate(weekStart)} – {Format.ShortDate(today)}", periods.Week.Centimeters);
            case ShareKind.Month:
                return Distance("Share.Card.Month", today.ToString("MMMM yyyy", L.Culture), periods.Month.Centimeters);
            case ShareKind.AllTime:
                var since = days.Count > 0 ? days[0].Date : today;
                return Distance("Share.Card.AllTime", L.Format("Share.Since", Format.Date(since)), periods.AllTime.Centimeters);
            case ShareKind.TopGames:
                {
                    var totals = GameStats.Summarize(_data.Catalog, _data.Store.GetApps(), _data.Store.GetAppUsage(), L["Games.Other"])
                        .Where(t => t.Category == AppCategory.Game).Take(IsStory ? 6 : 4).ToList();
                    var max = totals.Count == 0 ? 1 : totals.Max(t => t.Centimeters);
                    var rows = totals.Select((t, i) => new ShareRow(t.Name, Format.Distance(t.Centimeters), t.Centimeters / max, SeriesBrushes.For(i))).ToList();
                    var (number, unit) = Format.DistanceParts(totals.Sum(t => t.Centimeters));
                    return new ShareCardData(L["Share.Card.TopGames"], L["Share.Card.TopGamesSubtitle"], number, unit, string.Empty, rows, null, IsStory, footer);
                }

            default:
                {
                    var achievements = new AchievementsViewModel(_data);
                    achievements.Refresh();
                    var latest = achievements.Latest;
                    return latest is null
                        ? new ShareCardData(L["Share.Card.Achievement"], L["Share.NoAchievement"], string.Empty, string.Empty, string.Empty, [], "", IsStory, footer)
                        : new ShareCardData(L["Share.Card.Achievement"], latest.UnlockedText ?? string.Empty, latest.Name, string.Empty, latest.Description, [], latest.Glyph, IsStory, footer);
                }
        }
    }
}

/// <summary>Card kind option with a localized label.</summary>
public sealed record ShareKindOption(ShareKind Kind)
{
    public string Name => Loc.Instance[$"Share.Kind.{Kind}"];

    public override string ToString() => Name;
}

/// <summary>Renders a card view into a bitmap of an exact pixel size.</summary>
public static class ShareRenderer
{
    public static BitmapSource Render(ShareCardData data, int width, int height)
    {
        var card = new ShareCardView { DataContext = data, Width = width, Height = height };
        card.Measure(new Size(width, height));
        card.Arrange(new Rect(0, 0, width, height));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }
}
