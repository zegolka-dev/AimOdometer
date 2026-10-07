using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AimOdometer.Core.Games;

/// <summary>How an executable is treated in statistics.</summary>
public enum AppCategory
{
    /// <summary>A game: shown with its own name.</summary>
    Game = 0,

    /// <summary>Everything else, grouped as "Desktop &amp; apps".</summary>
    Other = 1,

    /// <summary>Hidden from statistics by the user (data is kept).</summary>
    Excluded = 2,
}

public enum GameSource
{
    Steam = 0,
    BuiltIn = 1,
    User = 2,
}

/// <summary>A game as shown in statistics. <see cref="Key"/> groups several executables into one game.</summary>
public sealed record GameInfo(string Key, string Name, GameSource Source, int? SteamAppId);

public sealed record AppClassification(AppCategory Category, GameInfo? Game)
{
    public static readonly AppClassification Other = new(AppCategory.Other, null);
    public static readonly AppClassification Excluded = new(AppCategory.Excluded, null);
}

/// <summary>A user's decision about one executable (stored in the database).</summary>
public sealed record AppRule(AppCategory Category, string? GameKey);

/// <summary>Entry of data/games.json.</summary>
public sealed record BuiltInGame(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("exe")] string[] Exe,
    [property: JsonPropertyName("pathContains")] string[]? PathContains,
    [property: JsonPropertyName("steamAppId")] int? SteamAppId);

/// <summary>Contents of data/games.json.</summary>
public sealed record BuiltInGameList(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("games")] BuiltInGame[] Games,
    [property: JsonPropertyName("steamNonGames")] int[] SteamNonGames)
{
    private const string ResourceName = "AimOdometer.Data.games.json";

    /// <summary>The list compiled into the app.</summary>
    public static BuiltInGameList LoadEmbedded()
    {
        using var stream = typeof(BuiltInGameList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize(stream, GamesJsonContext.Default.BuiltInGameList)
            ?? throw new InvalidDataException("games.json is empty.");
    }

    public static BuiltInGameList Parse(string json) =>
        JsonSerializer.Deserialize(json, GamesJsonContext.Default.BuiltInGameList)
            ?? throw new InvalidDataException("games.json is empty.");

    /// <summary>Adds entries from a user's games.json (same format); user entries win on equal ids.</summary>
    public BuiltInGameList MergedWith(BuiltInGameList? user) => user is null
        ? this
        : new BuiltInGameList(
            Version,
            [.. user.Games, .. Games.Where(g => user.Games.All(u => !string.Equals(u.Id, g.Id, StringComparison.OrdinalIgnoreCase)))],
            [.. SteamNonGames.Union(user.SteamNonGames ?? [])]);
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(BuiltInGameList))]
internal sealed partial class GamesJsonContext : JsonSerializerContext;

/// <summary>
/// Decides which game (if any) an executable belongs to. Order: the user's rules, then the Steam library
/// (exe inside a game's install folder), then the built-in list, otherwise "Desktop &amp; apps".
/// Runs in the UI and aggregation, never in the tracker, so the input hot path stays minimal.
/// </summary>
public sealed class GameCatalog
{
    private readonly (string Prefix, SteamApp App)[] _steamFolders;
    private readonly HashSet<int> _steamNonGames;
    private readonly BuiltInGame[] _builtIn;
    private readonly Dictionary<int, SteamApp> _steamById;
    private readonly IReadOnlyDictionary<long, AppRule> _rules;
    private readonly IReadOnlyDictionary<string, string> _customNames;

    public GameCatalog(
        IEnumerable<SteamApp> steamApps,
        BuiltInGameList builtIn,
        IReadOnlyDictionary<long, AppRule>? rules = null,
        IReadOnlyDictionary<string, string>? customNames = null)
    {
        ArgumentNullException.ThrowIfNull(steamApps);
        ArgumentNullException.ThrowIfNull(builtIn);
        var apps = steamApps.ToArray();

        // Longest folder first, so nested install folders resolve to the most specific game.
        _steamFolders = [.. apps
            .Select(a => (Prefix: Path.TrimEndingDirectorySeparator(a.InstallDirectory) + Path.DirectorySeparatorChar, App: a))
            .OrderByDescending(f => f.Prefix.Length)];
        _steamById = apps.ToDictionary(a => a.AppId);
        _steamNonGames = [.. builtIn.SteamNonGames];
        _builtIn = builtIn.Games;
        _rules = rules ?? new Dictionary<long, AppRule>();
        _customNames = customNames ?? new Dictionary<string, string>();
    }

    /// <summary>Optional user additions in the data folder, same format as data/games.json.</summary>
    public static string UserGamesPath => Path.Combine(AppIdentity.DataDirectory, "games.json");

    /// <summary>
    /// Builds the catalog for this PC: local Steam libraries, the built-in list merged with the user's games.json
    /// (if present and valid), and the user's rules and names from the database.
    /// </summary>
    public static GameCatalog Create(Storage.StatsStore store, string? userGamesPath = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var list = BuiltInGameList.LoadEmbedded();
        var userPath = userGamesPath ?? UserGamesPath;
        if (File.Exists(userPath))
        {
            try
            {
                list = list.MergedWith(BuiltInGameList.Parse(File.ReadAllText(userPath)));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Diagnostics.Log.Warning($"Ignoring invalid {userPath}: {ex.Message}");
            }
        }

        return new GameCatalog(SteamLibrary.LoadLocal(), list, store.GetAppRules(), store.GetGameNames());
    }

    public static string SteamKey(int appId) => string.Create(CultureInfo.InvariantCulture, $"steam:{appId}");

    /// <summary>Key for an executable the user marked as a game without picking an existing one.</summary>
    public static string ExeKey(string exePath) => "exe:" + Path.GetFileName(exePath).ToLowerInvariant();

    public AppClassification Classify(long appId, string exePath)
    {
        ArgumentNullException.ThrowIfNull(exePath);
        if (_rules.TryGetValue(appId, out var rule))
        {
            return rule.Category switch
            {
                AppCategory.Excluded => AppClassification.Excluded,
                AppCategory.Other => AppClassification.Other,
                _ => new AppClassification(AppCategory.Game, ResolveKey(rule.GameKey ?? ExeKey(exePath), exePath)),
            };
        }

        return Detect(exePath) is { } game ? new AppClassification(AppCategory.Game, game) : AppClassification.Other;
    }

    /// <summary>Automatic detection only (Steam, then built-in list), ignoring user rules.</summary>
    public GameInfo? Detect(string exePath)
    {
        foreach (var (prefix, app) in _steamFolders)
        {
            if (exePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return _steamNonGames.Contains(app.AppId) ? null : Named(new GameInfo(SteamKey(app.AppId), app.Name, GameSource.Steam, app.AppId));
            }
        }

        var fileName = Path.GetFileName(exePath);
        foreach (var game in _builtIn)
        {
            if (!game.Exe.Any(e => string.Equals(e, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (game.PathContains is { Length: > 0 } fragments
                && !fragments.Any(f => exePath.Contains(f, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return Named(game.SteamAppId is { } steamId
                ? new GameInfo(SteamKey(steamId), _steamById.TryGetValue(steamId, out var steam) ? steam.Name : game.Name, GameSource.BuiltIn, steamId)
                : new GameInfo(game.Id, game.Name, GameSource.BuiltIn, null));
        }

        return LauncherGame(exePath) is { } folder
            ? Named(new GameInfo("lib:" + folder.ToLowerInvariant(), folder, GameSource.BuiltIn, null))
            : null;
    }

    /// <summary>Game library folders of other launchers: everything installed there is a game.</summary>
    private static readonly string[] LauncherLibraries =
    [
        @"\EA Games\", @"\Origin Games\", @"\Epic Games\", @"\Ubisoft Game Launcher\games\", @"\GOG Galaxy\Games\",
        @"\GOG Games\", @"\XboxGames\", @"\Riot Games\", @"\Rockstar Games\",
    ];

    /// <summary>Launchers' own folders and helper programs that live next to games but are not games.</summary>
    private static readonly string[] NotGames =
    [
        "launcher", "crash", "setup", "install", "update", "redist", "anticheat", "battleye", "beservice", "uninst",
        "helper", "service", "report", "cefprocess", "overlay", "riot client", "riotclient", "epic online services",
    ];

    /// <summary>
    /// A game installed by EA app, Epic, Ubisoft Connect, GOG, Xbox / Game Pass, Riot or Rockstar that is in no list:
    /// the name of its folder in the launcher's library, or null.
    /// </summary>
    internal static string? LauncherGame(string exePath)
    {
        foreach (var library in LauncherLibraries)
        {
            var at = exePath.IndexOf(library, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var rest = exePath[(at + library.Length)..];
            var slash = rest.IndexOf('\\');
            if (slash <= 0)
            {
                return null; // an exe directly in the library folder
            }

            // The folder, the subfolders and the file name: an installer in "Game\__Installer\" is not the game.
            var folder = rest[..slash];
            return NotGames.Any(n => rest.Contains(n, StringComparison.OrdinalIgnoreCase)) ? null : folder;
        }

        return null;
    }

    /// <summary>Resolves a key chosen by the user (possibly an existing game's key, to merge executables).</summary>
    private GameInfo ResolveKey(string key, string exePath)
    {
        if (key.StartsWith("steam:", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
        {
            var name = _steamById.TryGetValue(appId, out var steam) ? steam.Name
                : _builtIn.FirstOrDefault(g => g.SteamAppId == appId)?.Name ?? key;
            return Named(new GameInfo(key, name, GameSource.User, appId));
        }

        var builtIn = _builtIn.FirstOrDefault(g => string.Equals(g.Id, key, StringComparison.OrdinalIgnoreCase));
        var fallbackName = builtIn?.Name ?? Path.GetFileNameWithoutExtension(exePath);
        return Named(new GameInfo(key, fallbackName, GameSource.User, builtIn?.SteamAppId));
    }

    private GameInfo Named(GameInfo game) =>
        _customNames.TryGetValue(game.Key, out var custom) ? game with { Name = custom } : game;
}
