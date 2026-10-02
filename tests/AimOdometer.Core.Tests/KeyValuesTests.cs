using AimOdometer.Core.Games;

namespace AimOdometer.Core.Tests;

public class KeyValuesTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"730"		"74000614728"
        			"228980"		"157818239"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        	}
        }
        """;

    private const string AppManifest = """
        "AppState"
        {
        	"appid"		"1172470"
        	"Universe"		"1"
        	"name"		"Apex Legends"
        	"installdir"		"Apex Legends"
        	"UserConfig"
        	{
        		"language"		"english"
        	}
        }
        """;

    [Fact]
    public void ParsesLibraryFolders()
    {
        var root = KeyValues.Parse(LibraryFolders);
        Assert.Equal("libraryfolders", root.Key);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", root["0"]!.GetString("path"));
        Assert.Equal("157818239", root["0"]!["apps"]!.GetString("228980"));
        Assert.Equal(string.Empty, root["0"]!.GetString("label"));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        var state = KeyValues.Parse(AppManifest);
        Assert.Equal("1172470", state.GetString("AppID"));
        Assert.Equal("1", state.GetString("universe"));
        Assert.Equal("english", state["userconfig"]!.GetString("LANGUAGE"));
    }

    [Fact]
    public void HandlesEscapesCommentsConditionalsAndUnquotedTokens()
    {
        var root = KeyValues.Parse("""
            // leading comment
            Root
            {
                "quote"   "say \"hi\""
                "tab"     "a\tb"    // trailing comment
                plain     value
                "win"     "yes" [$WIN32]
            }
            """);
        Assert.Equal("say \"hi\"", root.GetString("quote"));
        Assert.Equal("a\tb", root.GetString("tab"));
        Assert.Equal("value", root.GetString("plain"));
        Assert.Equal("yes", root.GetString("win"));
    }

    [Fact]
    public void MissingKeyReturnsNull()
    {
        Assert.Null(KeyValues.Parse(AppManifest).GetString("nope"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" { \"b\" }")]
    [InlineData("\"a\" \"unterminated")]
    [InlineData("}")]
    public void RejectsMalformedInput(string text)
    {
        Assert.Throws<FormatException>(() => KeyValues.Parse(text));
    }
}
