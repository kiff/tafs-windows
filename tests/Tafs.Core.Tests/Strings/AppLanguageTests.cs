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
}
