using AimOdometer.Core.Games;

namespace AimOdometer.Core.Tests;

public sealed class SteamLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aimodometer-steam-{Guid.NewGuid():N}");

    public SteamLibraryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Steam", "steamapps"));
        Directory.CreateDirectory(Path.Combine(_root, "Library2", "steamapps"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string SteamRoot => Path.Combine(_root, "Steam");

    private string Library2 => Path.Combine(_root, "Library2");

    private static string Escape(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    private void WriteLibraryFolders() => File.WriteAllText(Path.Combine(SteamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders"
        {
            "0" { "path" "{{Escape(SteamRoot)}}" }
            "1" { "path" "{{Escape(Library2)}}" }
            "2" { "path" "Z:\\Missing\\Library" }
        }
        """);

    private static void WriteManifest(string library, int appId, string name, string installDir) =>
        File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf"), $$"""
            "AppState"
            {
                "appid"      "{{appId}}"
                "name"       "{{name}}"
                "installdir" "{{installDir}}"
            }
            """);

    [Fact]
    public void FindsAllExistingLibraries()
    {
        WriteLibraryFolders();
        var libraries = SteamLibrary.FindLibraries(SteamRoot);
        Assert.Equal([SteamRoot, Library2], libraries);
    }

    [Fact]
    public void LoadsAppsFromEveryLibrary()
    {
        WriteLibraryFolders();
        WriteManifest(SteamRoot, 730, "Counter-Strike 2", "Counter-Strike Global Offensive");
        WriteManifest(Library2, 1172470, "Apex Legends", "Apex Legends");

        var apps = SteamLibrary.LoadInstalledApps(SteamRoot);

        Assert.Equal(2, apps.Count);
        Assert.Equal(Path.Combine(SteamRoot, "steamapps", "common", "Counter-Strike Global Offensive"), apps[0].InstallDirectory);
        Assert.Equal("Apex Legends", apps[1].Name);
        Assert.Equal(Path.Combine(Library2, "steamapps", "common", "Apex Legends"), apps[1].InstallDirectory);
    }

    [Fact]
    public void SkipsBrokenManifests()
    {
        WriteManifest(SteamRoot, 730, "Counter-Strike 2", "csgo");
        File.WriteAllText(Path.Combine(SteamRoot, "steamapps", "appmanifest_1.acf"), "\"AppState\" {");
        File.WriteAllText(Path.Combine(SteamRoot, "steamapps", "appmanifest_2.acf"), "\"AppState\" { \"appid\" \"2\" }");

        var apps = SteamLibrary.LoadInstalledApps(SteamRoot);

        Assert.Equal(730, Assert.Single(apps).AppId);
    }

    [Fact]
    public void WorksWithoutLibraryFoldersFile()
    {
        WriteManifest(SteamRoot, 570, "Dota 2", "dota 2 beta");
        Assert.Equal(570, Assert.Single(SteamLibrary.LoadInstalledApps(SteamRoot)).AppId);
    }
}
