using System.Text.Json;
using System.Text.RegularExpressions;

namespace AimOdometer.App.Tests;

/// <summary>
/// Checks the UI text rules from the brief: every string comes from the language files, every language has every key,
/// and format placeholders match between languages. Works on the source files, so it needs no running UI.
/// </summary>
public partial class LocalizationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string AppSource = Path.Combine(RepoRoot, "src", "AimOdometer.App");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AimOdometer.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> Language(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot, "data", "i18n", code + ".json")))!;

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "data", "i18n"), "*.json"))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    [GeneratedRegex(@"\{l:T ([A-Za-z0-9_.]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"(?:\bL|Loc\.Instance)(?:\[|\.Format\()""([A-Za-z0-9_.]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"new\(""([A-Z][A-Za-z]+\.[A-Za-z0-9_]+)""")]
    private static partial Regex RecordKey();

    [GeneratedRegex(@"\b(?:Text|Content|Header|ToolTip|Title)=""([^""{]*)""")]
    private static partial Regex LiteralAttribute();

    /// <summary>Icon-font glyphs such as &amp;#xE712; are symbols, not text.</summary>
    [GeneratedRegex(@"^(&#x[0-9A-Fa-f]+;)+$")]
    private static partial Regex IconGlyph();

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<string> UsedKeys()
    {
        foreach (var file in Directory.EnumerateFiles(AppSource, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
            {
                yield return m.Groups[1].Value;
            }
        }

        foreach (var file in Directory.EnumerateFiles(AppSource, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in CodeKey().Matches(text).Concat(RecordKey().Matches(text)))
            {
                yield return m.Groups[1].Value;
            }
        }
    }

    [Fact]
    public void EveryUsedKeyExistsInEnglish()
    {
        var english = Language("en");
        var missing = UsedKeys().Distinct().Where(k => !english.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Missing in en.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void DataDrivenKeysExist()
    {
        // Keys built at run time: achievements, comparisons, cities, map periods, gear kinds and share card kinds.
        var english = Language("en");
        var expected = new List<string>();
        using (var achievements = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "data", "achievements.json"))))
        {
            foreach (var a in achievements.RootElement.GetProperty("achievements").EnumerateArray())
            {
                var id = a.GetProperty("id").GetString();
                expected.Add($"Ach.{id}.Name");
                expected.Add($"Ach.{id}.Desc");
            }
        }

        using (var comparisons = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "data", "comparisons.json"))))
        {
            expected.AddRange(comparisons.RootElement.GetProperty("comparisons").EnumerateArray().Select(c => $"Cmp.{c.GetProperty("id").GetString()}"));
        }

        using (var cities = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "data", "cities.json"))))
        {
            expected.AddRange(cities.RootElement.GetProperty("cities").EnumerateArray().Select(c => $"City.{c.GetProperty("id").GetString()}"));
        }

        expected.AddRange(EnumMembers(Path.Combine(AppSource, "ViewModels", "MapViewModel.cs"), "MapPeriod").SelectMany(m => new[] { $"Map.Period.{m}", $"Map.Lead.{m}" }));
        expected.AddRange(EnumMembers(Path.Combine(RepoRoot, "src", "AimOdometer.Core", "Storage", "StatsStore.cs"), "GearKind").Select(m => $"Gear.Kind.{m}"));
        expected.AddRange(EnumMembers(Path.Combine(AppSource, "ViewModels", "ShareViewModel.cs"), "ShareKind").Select(m => $"Share.Kind.{m}"));
        expected.AddRange(["Share.Card.Day", "Share.Card.Week", "Share.Card.Month", "Share.Card.AllTime"]);
        expected.AddRange(SupportServiceIds().Select(id => $"Support.{id}.Note"));
        expected.AddRange(StreakTierId().Matches(File.ReadAllText(Path.Combine(RepoRoot, "src", "AimOdometer.Core", "Fun", "StreakTiers.cs")))
            .Select(m => $"Streak.Tier.{m.Groups[1].Value}"));

        Assert.True(expected.Count > 90);
        var missing = expected.Where(k => !english.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing in en.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void SupportServicesHaveUniqueIdsAndOnlyHttpsLinks()
    {
        var ids = SupportServiceIds().ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // A filled-in link must be a full https address; an empty one hides the service.
        var source = File.ReadAllText(Path.Combine(AppSource, "Services", "SupportLinks.cs"));
        foreach (Match m in SupportEntry().Matches(source))
        {
            var url = m.Groups["url"].Value;
            Assert.True(url.Length == 0 || url.StartsWith("https://", StringComparison.Ordinal), $"{m.Groups["id"].Value}: '{url}' is not an https link");
        }
    }

    private static IEnumerable<string> SupportServiceIds() =>
        SupportEntry().Matches(File.ReadAllText(Path.Combine(AppSource, "Services", "SupportLinks.cs"))).Select(m => m.Groups["id"].Value);

    [GeneratedRegex("""new\("(?<id>[a-z]+)", "[^"]+", "(?<url>[^"]*)",""")]
    private static partial Regex SupportEntry();

    [GeneratedRegex("""new\(\d+, \d+, "([a-z]+)"\)""")]
    private static partial Regex StreakTierId();

    private static IEnumerable<string> EnumMembers(string file, string name)
    {
        var text = File.ReadAllText(file);
        var start = text.IndexOf("enum " + name, StringComparison.Ordinal);
        var body = text[(text.IndexOf('{', start) + 1)..text.IndexOf('}', start)];
        return body.Split(',').Select(m => m.Split('=')[0].Trim()).Where(m => m.Length > 0 && char.IsLetter(m[0]));
    }

    [Fact]
    public void FindsKeysInSources()
    {
        Assert.True(UsedKeys().Distinct().Count() > 100);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void LanguageHasExactlyTheEnglishKeys(string code)
    {
        var english = Language("en").Keys.ToHashSet();
        var language = Language(code).Keys.ToHashSet();
        Assert.Empty(english.Except(language));
        Assert.Empty(language.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void PlaceholdersMatchEnglish(string code)
    {
        var english = Language("en");
        foreach (var (key, value) in Language(code))
        {
            var expected = Placeholder().Matches(english[key]).Select(m => m.Groups[1].Value).Order();
            var actual = Placeholder().Matches(value).Select(m => m.Groups[1].Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"{code}.json '{key}' has different placeholders than en.json");
        }
    }

    [Fact]
    public void XamlHasNoHardcodedText()
    {
        // Brand name and pure punctuation are allowed; anything with letters must come from the language files.
        string[] allowed = ["AimOdometer"];
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(AppSource, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in LiteralAttribute().Matches(File.ReadAllText(file)))
            {
                var value = m.Groups[1].Value;
                if (value.Any(char.IsLetter) && !allowed.Contains(value) && !IconGlyph().IsMatch(value))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Hardcoded UI text: " + string.Join("; ", offenders));
    }
}
