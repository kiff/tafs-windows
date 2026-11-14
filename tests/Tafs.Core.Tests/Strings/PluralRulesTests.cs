using Tafs.Core.Words;

namespace Tafs.Core.Tests.Strings;

[Part("strings")]
public class PluralRulesTests
{
    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("de", 1, "one")]
    [InlineData("nl", 5, "other")]
    [InlineData("es", 1, "one")]
    [InlineData("it", 1, "one")]
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    [InlineData("pt", 0, "one")]
    [InlineData("pt", 2, "other")]
    [InlineData("pl", 0, "many")]
    [InlineData("pl", 1, "one")]
    [InlineData("pl", 2, "few")]
    [InlineData("pl", 4, "few")]
    [InlineData("pl", 5, "many")]
    [InlineData("pl", 12, "many")]
    [InlineData("pl", 14, "many")]
    [InlineData("pl", 22, "few")]
    [InlineData("pl", 100, "many")]
    [InlineData("pl", 102, "few")]
    [InlineData("pl", -3, "few")]
    public void A_number_takes_the_form_its_language_gives_it(string language, long count, string form) =>
        Assert.Equal(form, PluralRules.Form(language, count));

    [Fact]
    public void An_unknown_language_has_no_rule() =>
        Assert.Throws<ArgumentException>(() => PluralRules.Form("xx", 1));
}
