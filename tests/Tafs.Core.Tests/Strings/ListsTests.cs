using Tafs.Core.Words;
namespace Tafs.Core.Tests.Strings;

[Part("strings")]
public class ListsTests
{
    [Theory]
    [InlineData("en", "Refunds and Chargebacks", "Refunds, Chargebacks, and Invoices")]
    [InlineData("de", "Refunds und Chargebacks", "Refunds, Chargebacks und Invoices")]
    [InlineData("fr", "Refunds et Chargebacks", "Refunds, Chargebacks et Invoices")]
    [InlineData("es", "Refunds y Chargebacks", "Refunds, Chargebacks e Invoices")]
    [InlineData("it", "Refunds e Chargebacks", "Refunds, Chargebacks e Invoices")]
    [InlineData("pt", "Refunds e Chargebacks", "Refunds, Chargebacks e Invoices")]
    [InlineData("nl", "Refunds en Chargebacks", "Refunds, Chargebacks en Invoices")]
    [InlineData("pl", "Refunds i Chargebacks", "Refunds, Chargebacks i Invoices")]
    [InlineData("ja", "Refunds、Chargebacks", "Refunds、Chargebacks、Invoices")]
    [InlineData("zh-Hant", "Refunds和Chargebacks", "Refunds、Chargebacks和Invoices")]
    [InlineData("ar", "Refunds وChargebacks", "Refunds وChargebacks وInvoices")]
    [InlineData("he", "Refunds ו-Chargebacks", "Refunds, Chargebacks ו-Invoices")]
    [InlineData("cs", "Refunds a Chargebacks", "Refunds, Chargebacks a Invoices")] // CLDR keeps a one-letter word with the next
    public void Names_join_as_CLDR_s_and_list(string language, string two, string three)
    {
        Assert.Equal("Refunds", Lists.And(language, ["Refunds"]));
        Assert.Equal(two, Lists.And(language, ["Refunds", "Chargebacks"]));
        Assert.Equal(three, Lists.And(language, ["Refunds", "Chargebacks", "Invoices"]));
    }

    [Theory]
    [InlineData("Iberia", "Ventas e Iberia")]
    [InlineData("Hilton", "Ventas e Hilton")]
    [InlineData("hielo", "Ventas y hielo")]
    public void Spanish_says_e_before_an_i_sound(string last, string joined) => Assert.Equal(joined, Lists.And("es", ["Ventas", last]));
}

[Part("strings")]
public class HebrewListTests
{
    [Fact]
    public void Hebrew_joins_a_Hebrew_word_with_no_hyphen() => Assert.Equal("החזרים ושעות", Lists.And("he", ["החזרים", "שעות"]));
}
