using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafCardTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly CardWindow Day = CardWindow.Of(En, "calendar_day");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    static readonly CardRule Refunds = new("refund", "Refunds", Day, KiffAmount.Euros(150), KiffAmount.Euros(2_000));

    static string Hero(KiffAmount limit, KiffAmount? used)
    {
        var (figure, words) = new TafCard("c", "Refunds", [Refunds], new CardTotal(limit, used, Day)).Hero(En)!.Value;
        return $"{figure} {words}";
    }

    [Fact]
    public void Its_figure_is_what_s_left_of_KIFF_s_total()
    {
        Assert.Equal("€1,520 left today of €2,000", Hero(KiffAmount.Euros(2_000), KiffAmount.Euros(480)));
        Assert.Equal("€0 left today of €100", Hero(KiffAmount.Euros(100), KiffAmount.Euros(140)));
        Assert.Equal("€12.50 left today of €20", Hero(new(2000, "EUR", 2), new(750, "EUR", 2)));
        Assert.Equal("1,500 credits left today of 2,000 credits", Hero(new(2_000, "credits", 0), new(500, "credits", 0)));
    }

    /// <summary>The iPad's TafCard.hero: no left figure unless the use is known in the limit's own unit and scale.</summary>
    [Fact]
    public void With_its_use_unknown_or_in_another_unit_it_shows_the_limit_alone()
    {
        Assert.Equal("€2,000 a day · use unavailable", Hero(KiffAmount.Euros(2_000), null));
        Assert.Equal("€2,000 a day · use unavailable", Hero(KiffAmount.Euros(2_000), new(4000, "USD", 2)));
        Assert.Equal("€2,000 a day · use unavailable", Hero(KiffAmount.Euros(2_000), new(40, "EUR", 0)));
    }

    [Fact]
    public void A_bare_number_stands_bare_and_says_so_once()
    {
        Assert.Equal("29,000 left today of 30,000 · no currency", Hero(new(30_000, null, 2), new(1_000, "", 0)));
        Assert.Equal("30,000 a day · use unavailable · no currency", Hero(new(30_000, null, 0), null));
        Assert.Equal("30,000 (no currency)", new KiffAmount(30_000, null, 2).Formatted(En));
    }

    [Fact]
    public void Its_calls_show_when_it_has_no_money_total()
    {
        var reads = new TafCard("r", "Read Stripe", [], Calls: new CallsTotal(200, 20, Day));
        Assert.Equal(("180", "calls left today of 200"), reads.Hero(En));
        Assert.Equal(("1", "call left today of 200"), (reads with { Calls = new CallsTotal(200, 199, Day) }).Hero(En));
        Assert.Equal(("200", "calls a day · use unavailable"), (reads with { Calls = new CallsTotal(200, null, Day) }).Hero(En));
        Assert.Null(new TafCard("n", "Nothing", []).Hero(En));
    }

    [Fact]
    public void A_rule_s_limits_read_as_KIFF_s_Cards_say_them()
    {
        Assert.Equal("up to €150 each; more asks you first · €2,000 in total a day, then it asks you", Refunds.Summary(En));
        Assert.Equal("Outside: held for you", Refunds.Outside(En));
        Assert.Equal("≤ €150 each · €2,000 a day", (Refunds with { Holds = false }).Summary(En));
        Assert.Equal("Outside: refused", (Refunds with { Holds = false }).Outside(En));
        Assert.Equal("never", (Refunds with { Never = true }).Summary(En));
        Assert.Equal("always asks you", (Refunds with { AlwaysAsk = true }).Summary(En));
        var calls = new CardRule("list_charges", "List charges", Day, CountPerWindow: 200);
        Assert.Equal("200 calls a day, then it asks you", calls.Summary(En));
        Assert.Equal("Each call asks you first", (calls with { CountPerWindow = 0 }).Summary(En));
        Assert.Equal("200 calls a day", (calls with { Holds = false }).Summary(En));
        Assert.Equal("10 (no currency) in total a day", new CardRule("x", "X", Day, PerWindow: new(10, null, 0), Holds: false).Summary(En));
    }

    [Fact]
    public void Its_facts_are_its_main_rule_and_its_tools_or_without_a_figure_its_rules()
    {
        var credits = Refunds with { Action = "credit", Label = "Credits", PerAction = KiffAmount.Euros(100) };
        var card = new TafCard("c", "Refunds · Stripe", [Refunds, credits], new CardTotal(KiffAmount.Euros(2_000), KiffAmount.Euros(480), Day));
        Assert.Equal(new[] { "up to €150 each; more asks you first", "2 tools" }, card.Facts(En));

        var noFigure = card with { Total = null, Rules = [Refunds, credits, Refunds with { Label = "Payouts" }] };
        Assert.Equal(new[] { "refunds up to €150 each; more asks you first · €2,000 in total a day, then it asks you",
            "credits up to €100 each; more asks you first · €2,000 in total a day, then it asks you", "1 more" }, noFigure.Facts(En));
    }

    [Fact]
    public void Its_bar_names_it_with_its_environment_and_the_taf_s_other_Cards()
    {
        var card = new TafCard("c", "Refunds · Stripe test", [Refunds], new CardTotal(KiffAmount.Euros(2_000), KiffAmount.Euros(480), Day),
            Environment: "test", Live: false);
        Assert.Equal("TEST", card.EnvironmentLabel);
        Assert.Equal(new CardBarLine("€1,520 left today of €2,000", "Refunds · Stripe test · Test · 3 Cards"), card.Bar(En, 3));
        Assert.Null((card with { Live = true }).EnvironmentLabel);
        Assert.Equal(new CardBarLine("Nothing", "Nothing"), new TafCard("n", "Nothing", []).Bar(En, 1));
    }

    [Fact]
    public void A_window_it_doesnt_know_reads_as_sent_and_tafs_cloud_s_words_come_first()
    {
        Assert.Equal(new CardWindow("fortnight", "fortnight", "fortnight"), CardWindow.Of(En, "fortnight"));
        Assert.Equal("in any hour", CardWindow.Of(En, "rolling_1h").SumLabel(En));
        Assert.Equal("this sprint", CardWindow.Of(En, "rolling_1h", now: "this sprint").Current);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void The_sample_s_Card_reads_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var card = TafScreen.SampleOwnCard(w);
        Assert.All(card.Rules, r => Assert.DoesNotContain("{", r.Summary(w)));
        Assert.DoesNotContain("{", card.Bar(w, 3).Hero + card.Bar(w, 3).Under);
        Assert.All(card.Facts(w), f => Assert.DoesNotContain("{", f));
    }
}
