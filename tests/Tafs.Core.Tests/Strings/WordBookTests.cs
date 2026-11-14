using Tafs.Core.Words;

namespace Tafs.Core.Tests.Strings;

/// <summary>This app's words, read from the pinned shared repository: every shipped language, every key.</summary>
[Part("strings")]
public class WordBookTests
{
    static readonly IReadOnlyList<string> Shipped = WordBook.Shipped(SharedFiles.Strings);

    public static IEnumerable<object[]> Languages() => Shipped.Select(l => new object[] { l });

    [Fact]
    public void The_eight_languages_of_the_spec_are_shipped() =>
        Assert.Equal(new[] { "de", "en", "es", "fr", "it", "nl", "pl", "pt" }, Shipped.Order());

    [Theory, MemberData(nameof(Languages))]
    public void Every_shipped_language_has_every_english_key(string language)
    {
        var english = WordBook.Load(SharedFiles.Strings, "en");
        var book = WordBook.Load(SharedFiles.Strings, language);
        Assert.Empty(english.Keys.Where(k => !book.Has(k)));
        Assert.Empty(book.Keys.Where(k => !english.Has(k)));
    }

    [Theory, MemberData(nameof(Languages))]
    public void A_line_fills_the_same_values_in_every_language(string language)
    {
        var english = WordBook.Load(SharedFiles.Strings, "en");
        var book = WordBook.Load(SharedFiles.Strings, language);
        var different = english.Keys.Where(k => !english.Placeholders(k).SetEquals(book.Placeholders(k))).ToList();
        Assert.Empty(different);
    }

    [Theory, MemberData(nameof(Languages))]
    public void A_count_has_the_forms_its_language_asks_for_and_other(string language)
    {
        var book = WordBook.Load(SharedFiles.Strings, language);
        var asked = new[] { 0L, 1, 2, 3, 5, 12, 22, 100, 102 }.Select(n => PluralRules.Form(language, n)).ToHashSet();
        var counts = book.Keys.Where(k => book.Forms(k).Count > 0).ToList();
        Assert.NotEmpty(counts);
        Assert.All(counts, k =>
        {
            Assert.Contains("other", book.Forms(k));
            Assert.True(asked.IsSubsetOf(book.Forms(k)), $"{language}: '{k}' lacks a form its numbers take");
        });
    }

    [Theory, MemberData(nameof(Languages))]
    public void Only_a_language_with_a_possessive_rule_uses_it(string language)
    {
        var book = WordBook.Load(SharedFiles.Strings, language);
        var users = book.Keys.Where(book.UsesPossessive).ToList();
        if (language is "en" or "de") Assert.NotEmpty(users);
        else Assert.Empty(users);
    }

    [Fact]
    public void A_line_fills_its_values()
    {
        var en = WordBook.Load(SharedFiles.Strings, "en");
        Assert.Equal("Round up to 12 €", en.Get("needs_you.allow_round_up", ("amount", "12 €")));
        Assert.Equal("answer by 18:00", en.Get("needs_you.answer_by", ("time", "18:00")));
    }

    [Fact]
    public void A_missing_value_is_an_error_not_a_hole()
    {
        var en = WordBook.Load(SharedFiles.Strings, "en");
        var e = Assert.Throws<ArgumentException>(() => en.Get("needs_you.answer_by"));
        Assert.Contains("{time}", e.Message);
    }

    [Fact]
    public void A_missing_key_is_an_error_not_english()
    {
        var fr = WordBook.Load(SharedFiles.Strings, "fr");
        Assert.Throws<KeyNotFoundException>(() => fr.Get("needs_you.no_such_line"));
    }

    [Fact]
    public void A_count_takes_its_language_s_form()
    {
        var en = WordBook.Load(SharedFiles.Strings, "en");
        var pl = WordBook.Load(SharedFiles.Strings, "pl");
        Assert.Equal("1 request needs you", en.Count("work.requests_need_you", 1));
        Assert.Equal("3 requests need you", en.Count("work.requests_need_you", 3));
        Assert.Contains("3", pl.Count("work.requests_need_you", 3));
    }

    [Theory]
    [InlineData("en", "Refunds", "Refunds'")]
    [InlineData("en", "Rae", "Rae's")]
    [InlineData("de", "Klaus", "Klaus'")]
    [InlineData("de", "Rae", "Raes")]
    [InlineData("fr", "Rae", "Rae")]
    public void A_name_takes_its_language_s_possessive(string language, string name, string expected) =>
        Assert.Equal(expected, WordBook.Load(SharedFiles.Strings, language).PossessiveOf(name));
}
