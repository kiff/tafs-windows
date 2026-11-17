using Tafs.Core.Words;

namespace Tafs.Core.Tests.Strings;

[Part("strings")]
public class AppLanguageTests
{
    static readonly string[] Shipped = { "en", "de", "fr", "es", "it", "pt", "nl", "pl" };

    [Theory]
    [InlineData(new[] { "fr-FR", "en-US" }, "fr")]
    [InlineData(new[] { "pt-BR" }, "pt")]
    [InlineData(new[] { "ja-JP", "de-AT" }, "de")]
    [InlineData(new[] { "ja-JP" }, "en")]
    [InlineData(new string[0], "en")]
    [InlineData(new[] { "NL" }, "nl")]
    public void The_first_shipped_language_of_the_person_s_wins(string[] preferred, string expected) =>
        Assert.Equal(expected, AppLanguage.Pick(preferred, Shipped));

    /// <summary>The picker's list: the 8 in the Terms table's order, each named in itself, as Android's.</summary>
    [Fact]
    public void The_picker_lists_tafs_s_languages_each_named_in_itself()
    {
        Assert.Equal(Shipped, AppLanguage.Named.Select(l => l.Code));
        Assert.Equal(["English", "Deutsch", "Français", "Español", "Italiano", "Português", "Nederlands", "Polski"], AppLanguage.Named.Select(l => l.Name));
    }

    /// <summary>A kept choice wins over this PC's languages; one tafs doesn't ship, or none, falls back to them; --lang wins over both.</summary>
    [Fact]
    public void The_kept_choice_wins_and_an_odd_one_falls_back()
    {
        Assert.Equal("pl", AppLanguage.Resolve(null, "pl", ["fr-FR"], Shipped));
        Assert.Equal("de", AppLanguage.Resolve(null, "de-DE", ["fr-FR"], Shipped));
        Assert.Equal("fr", AppLanguage.Resolve(null, "ja", ["fr-FR"], Shipped));
        Assert.Equal("fr", AppLanguage.Resolve(null, null, ["fr-FR"], Shipped));
        Assert.Equal("fr", AppLanguage.Resolve(null, "  ", ["fr-FR"], Shipped));
        Assert.Equal("it", AppLanguage.Resolve("it", "pl", ["fr-FR"], Shipped));
        Assert.Null(AppLanguage.Kept("xx", Shipped));
    }
}
