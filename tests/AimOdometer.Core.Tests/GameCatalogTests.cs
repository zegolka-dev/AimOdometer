using AimOdometer.Core.Games;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public class GameCatalogTests
{
    private const string SteamCommon = @"C:\Program Files (x86)\Steam\steamapps\common";

    private static readonly SteamApp[] Steam =
    [
        new(730, "Counter-Strike 2", SteamCommon + @"\Counter-Strike Global Offensive"),
        new(1172470, "Apex Legends", SteamCommon + @"\Apex Legends"),
        new(431960, "Wallpaper Engine", SteamCommon + @"\wallpaper_engine"),
        new(10, "Outer", @"D:\Games\Outer"),
        new(11, "Inner", @"D:\Games\Outer\Inner"),
    ];

    private static GameCatalog Catalog(
        IReadOnlyDictionary<long, AppRule>? rules = null, IReadOnlyDictionary<string, string>? names = null) =>
        new(Steam, BuiltInGameList.LoadEmbedded(), rules, names);

    [Fact]
    public void EmbeddedListIsValid()
    {
        var list = BuiltInGameList.LoadEmbedded();
        Assert.True(list.Games.Length >= 40);
        Assert.Equal(list.Games.Length, list.Games.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(list.Games, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.NotEmpty(g.Exe);
            Assert.All(g.Exe, e => Assert.EndsWith(".exe", e, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void SteamGame_IsDetectedByInstallFolder()
    {
        var result = Catalog().Classify(1, SteamCommon + @"\Counter-Strike Global Offensive\game\bin\win64\cs2.exe");
        Assert.Equal(AppCategory.Game, result.Category);
        Assert.Equal("steam:730", result.Game!.Key);
        Assert.Equal("Counter-Strike 2", result.Game.Name);
        Assert.Equal(GameSource.Steam, result.Game.Source);
    }

    [Fact]
    public void SteamFolderMatch_RequiresWholeFolderName()
    {
        // "Apex Legends Tools" must not match "Apex Legends".
        Assert.Equal(AppCategory.Other, Catalog().Classify(1, SteamCommon + @"\Apex Legends Tools\x.exe").Category);
    }

    [Fact]
    public void NestedSteamFolders_PickMostSpecific()
    {
        Assert.Equal("steam:11", Catalog().Detect(@"D:\Games\Outer\Inner\game.exe")!.Key);
        Assert.Equal("steam:10", Catalog().Detect(@"D:\Games\Outer\game.exe")!.Key);
    }

    [Fact]
    public void SteamTools_AreNotGames()
    {
        Assert.Equal(AppCategory.Other, Catalog().Classify(1, SteamCommon + @"\wallpaper_engine\wallpaper64.exe").Category);
    }

    [Fact]
    public void BuiltInGame_IsDetectedByExeNameAnywhere()
    {
        var game = Catalog().Detect(@"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
        Assert.Equal("valorant", game!.Key);
        Assert.Equal(GameSource.BuiltIn, game.Source);
    }

    [Fact]
    public void BuiltInGame_ExeNameIsCaseInsensitive()
    {
        Assert.Equal("osu", Catalog().Detect(@"C:\Users\x\AppData\Local\osu!\OSU!.EXE")!.Key);
    }

    [Fact]
    public void BuiltInGame_WithSteamId_SharesTheSteamKey()
    {
        // Apex installed through the EA app counts together with the Steam version.
        var game = Catalog().Detect(@"C:\Program Files\EA Games\Apex\r5apex.exe");
        Assert.Equal("steam:1172470", game!.Key);
        Assert.Equal("Apex Legends", game.Name);
    }

    [Fact]
    public void PathContains_DisambiguatesGenericExeNames()
    {
        Assert.Equal("minecraft-java", Catalog().Detect(@"C:\Users\x\AppData\Roaming\.minecraft\runtime\java-runtime-gamma\bin\javaw.exe")!.Key);
        Assert.Null(Catalog().Detect(@"C:\Program Files\Java\jdk-21\bin\javaw.exe"));
    }

    [Fact]
    public void UnknownExe_IsOther()
    {
        Assert.Equal(AppClassification.Other, Catalog().Classify(1, @"C:\Windows\explorer.exe"));
    }

    [Fact]
    public void UserRule_ExcludeWins()
    {
        var rules = new Dictionary<long, AppRule> { [7] = new(AppCategory.Excluded, null) };
        Assert.Equal(AppCategory.Excluded, Catalog(rules).Classify(7, SteamCommon + @"\Apex Legends\r5apex.exe").Category);
    }

    [Fact]
    public void UserRule_NotAGame_OverridesDetection()
    {
        var rules = new Dictionary<long, AppRule> { [7] = new(AppCategory.Other, null) };
        Assert.Equal(AppCategory.Other, Catalog(rules).Classify(7, SteamCommon + @"\Apex Legends\r5apex.exe").Category);
    }

    [Fact]
    public void UserRule_MarkExeAsNewGame()
    {
        var rules = new Dictionary<long, AppRule> { [7] = new(AppCategory.Game, null) };
        var game = Catalog(rules).Classify(7, @"D:\Indie\MyShooter.exe").Game!;
        Assert.Equal("exe:myshooter.exe", game.Key);
        Assert.Equal("MyShooter", game.Name);
        Assert.Equal(GameSource.User, game.Source);
    }

    [Fact]
    public void UserRule_MergeIntoExistingGame()
    {
        var rules = new Dictionary<long, AppRule> { [7] = new(AppCategory.Game, "steam:730") };
        var game = Catalog(rules).Classify(7, @"D:\Tools\cs2_launcher.exe").Game!;
        Assert.Equal("steam:730", game.Key);
        Assert.Equal("Counter-Strike 2", game.Name);
    }

    [Fact]
    public void CustomName_RenamesAnyGame()
    {
        var names = new Dictionary<string, string> { ["steam:730"] = "CS" };
        Assert.Equal("CS", Catalog(names: names).Classify(1, SteamCommon + @"\Counter-Strike Global Offensive\cs2.exe").Game!.Name);
    }

    [Fact]
    public void UserList_MergesWithBuiltIn()
    {
        var user = BuiltInGameList.Parse("""
            { "version": 1, "games": [ { "id": "valorant", "name": "Valo", "exe": ["valo.exe"] },
                                       { "id": "mine", "name": "Mine", "exe": ["mine.exe"] } ], "steamNonGames": [730] }
            """);
        var merged = BuiltInGameList.LoadEmbedded().MergedWith(user);
        var catalog = new GameCatalog(Steam, merged);

        Assert.Equal("Mine", catalog.Detect(@"C:\mine.exe")!.Name);
        Assert.Equal("Valo", catalog.Detect(@"C:\valo.exe")!.Name);
        Assert.Null(catalog.Detect(@"C:\VALORANT-Win64-Shipping.exe")); // user entry replaced the built-in one
        Assert.Null(catalog.Detect(SteamCommon + @"\Counter-Strike Global Offensive\cs2.exe")); // marked as non-game
    }

    [Fact]
    public void Summarize_GroupsAppsIntoGamesAndOther()
    {
        AppRecord[] apps =
        [
            new(1, SteamCommon + @"\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", "cs2.exe"),
            new(2, @"C:\Windows\explorer.exe", "explorer.exe"),
            new(3, @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox.exe"),
            new(4, @"D:\Secret\game.exe", "game.exe"),
        ];
        AppUsage[] usage =
        [
            new(1, Centimeters: 100_000, 0, 0, Clicks: 50, 0, 0, ForegroundSeconds: 3600, PeakSpeedCmPerSecond: 200),
            new(2, Centimeters: 300, 0, 0, Clicks: 5, 0, 0, ForegroundSeconds: 60, PeakSpeedCmPerSecond: 20),
            new(3, Centimeters: 700, 0, 0, Clicks: 9, 0, 0, ForegroundSeconds: 120, PeakSpeedCmPerSecond: 30),
            new(4, Centimeters: 9999, 0, 0, Clicks: 1, 0, 0, ForegroundSeconds: 1, PeakSpeedCmPerSecond: 1),
            new(0, Centimeters: 50, 0, 0, Clicks: 0, 0, 0, ForegroundSeconds: 0, PeakSpeedCmPerSecond: 5),
        ];
        var rules = new Dictionary<long, AppRule> { [4] = new(AppCategory.Excluded, null) };

        var totals = GameStats.Summarize(Catalog(rules), apps, usage, "Desktop & apps");

        Assert.Equal(2, totals.Count);
        var cs = totals[0];
        Assert.Equal("Counter-Strike 2", cs.Name);
        Assert.Equal(1.0, cs.KilometersPerHour!.Value, precision: 9);
        var other = totals[1];
        Assert.Equal(GameTotal.OtherKey, other.Key);
        Assert.Equal(1050, other.Centimeters, precision: 9);
        Assert.Equal(14, other.Clicks);
        Assert.Equal(["explorer.exe", "firefox.exe"], other.Executables);
    }

    [Fact]
    public void KilometersPerHour_NeedsAtLeastAMinute()
    {
        var total = new GameTotal("k", "n", AppCategory.Game, 1000, 59, 0, 0, []);
        Assert.Null(total.KilometersPerHour);
    }
}
