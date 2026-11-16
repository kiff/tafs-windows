using Tafs.Core.Tests.Ci;
using System.Text.RegularExpressions;

namespace Tafs.Core.Tests.Strings;

/// <summary>
/// Rule 14 on the approved drawings, as iOS's IllustrationsTests: shapes only. The drawings' file holds no text element, no
/// image and no string but the theme's keys, so no drawing can carry a word, in any language; and each screen draws its board.
/// </summary>
[Part("strings")]
public class IllustrationsRuleTests
{
    static string App(string file) => File.ReadAllText(Path.Combine(RepoFiles.Root, "src", "Tafs.App", file));

    /// <summary>The file's code, without its comments: what's drawn, not what's said about it.</summary>
    static string Code(string source) =>
        string.Join('\n', source.Split('\n').Select(l => Regex.Replace(l, @"//.*$", "")));

    [Fact]
    public void The_drawings_have_no_words()
    {
        var code = Code(App("Illustrations.cs"));
        Assert.DoesNotContain("TextBlock", code);
        Assert.DoesNotContain("Image", code);
        Assert.DoesNotContain("Glyph", code);
        Assert.DoesNotContain("App.Words", code);
        var literals = Regex.Matches(code, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(literals);
        Assert.All(literals, l => Assert.Matches("^Tafs[A-Za-z]+$", l));
    }

    [Theory]
    [InlineData("SignedOutPage.xaml.cs", "FirstRun")]
    [InlineData("NeedsYouPage.xaml.cs", "NothingWaiting")]
    [InlineData("OverviewPage.xaml.cs", "Overview")]
    [InlineData("OverviewPage.xaml.cs", "NoTafs")]
    [InlineData("BriefPage.xaml.cs", "BriefClear")]
    [InlineData("HaltPage.xaml.cs", "AllHalted")]
    [InlineData("CoachPanel.xaml.cs", "CoachStarting")]
    [InlineData("CoachPanel.Chats.cs", "CoachFirst")]
    [InlineData("GalleryPage.xaml.cs", "NewTaf")]
    public void Each_screen_draws_its_board(string file, string board) =>
        Assert.Contains($"Illustrations.{board}()", Code(App(file)));
}
