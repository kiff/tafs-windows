using Tafs.Core.Words;

namespace Tafs.Core.Tests.Strings;

[Part("strings")]
public class AppLanguageTests
{
    static readonly string[] Shipped = { "en", "de", "fr", "es", "it", "pt", "pt-BR", "nl", "pl", "ko" };

    [Theory]
    [InlineData(new[] { "fr-FR", "en-US" }, "fr")]
    [InlineData(new[] { "pt-BR" }, "pt-BR")]
    [InlineData(new[] { "pt-PT" }, "pt")]
    [InlineData(new[] { "pt" }, "pt")]
    [InlineData(new[] { "ko-KR" }, "ko")]
    [InlineData(new[] { "ja-JP", "de-AT" }, "de")]
    [InlineData(new[] { "ja-JP" }, "en")]
    [InlineData(new string[0], "en")]
    [InlineData(new[] { "NL" }, "nl")]
    public void The_first_shipped_language_of_the_person_s_wins(string[] preferred, string expected) =>
        Assert.Equal(expected, AppLanguage.Pick(preferred, Shipped));

    /// <summary>The picker's list: tafs's languages in the Terms table's order, each named in itself, as Android's.</summary>
    [Fact]
    public void The_picker_lists_tafs_s_languages_each_named_in_itself()
    {
        var picker = AppLanguage.Picker(Shipped);
        Assert.Equal(["en", "de", "fr", "es", "it", "pt", "nl", "pl", "ko", "pt-BR"], picker.Select(l => l.Code));
        Assert.Equal(["English", "Deutsch", "Français", "Español", "Italiano", "Português", "Nederlands", "Polski", "한국어", "Português (Brasil)"], picker.Select(l => l.Name));
    }

    /// <summary>Turkmen and Hindi named in themselves, as Android's, after Lao.</summary>
    [Fact]
    public void Turkmen_and_Hindi_are_named_in_themselves()
    {
        var named = AppLanguage.Named.Select(l => l.Code).ToList();
        Assert.Equal(["lo", "tk", "hi"], named.Skip(named.IndexOf("lo")).Take(3));
        Assert.Equal(["Türkmen dili", "हिन्दी"], AppLanguage.Named.Where(l => l.Code is "tk" or "hi").Select(l => l.Name));
    }

    /// <summary>Every language tafs ships at the pin has its name, so the picker never drops one.</summary>
    [Fact]
    public void Every_shipped_language_at_the_pin_is_named()
    {
        var shipped = WordBook.Shipped(SharedFiles.Strings);
        Assert.Equal(shipped.Order(StringComparer.Ordinal), AppLanguage.Picker(shipped).Select(l => l.Code).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-Hans-CN", null)]
    [InlineData("zh-CN", null)]
    [InlineData("sr-Latn-RS", "sr-Latn")]
    [InlineData("sr-Cyrl-RS", null)]
    [InlineData("sr-RS", null)]
    [InlineData("he-IL", "he")]
    [InlineData("ar-EG", "ar")]
    public void A_Windows_tag_reads_as_the_language_tafs_ships(string tag, string? expected) =>
        Assert.Equal(expected, AppLanguage.Kept(tag, ["en", "zh-Hant", "sr-Latn", "he", "ar"]));

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
        Assert.Equal("pt-BR", AppLanguage.Kept("pt-BR", Shipped));
        Assert.Equal("pt", AppLanguage.Kept("pt-PT", Shipped));
    }
}
