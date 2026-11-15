using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafCardsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>
    /// A taf with no KIFF Card listed shows one merged from its rules, as the iPad's shownCards: its name, its rules, its
    /// first limit over a window with what was used (none when nothing's said, unknown only when KIFF couldn't read it).
    /// </summary>
    [Fact]
    public void A_taf_with_no_Card_listed_shows_one_merged_from_its_rules()
    {
        JsonElement Of(string json) => JsonDocument.Parse(json).RootElement;
        const string rules = """ "rules": [{"action": "stripe_refund", "label": "Create refund", "per_window": {"minor": 100000, "unit": "EUR", "scale": 2}, "window": "calendar_day"}]""";
        var merged = Assert.Single(TafCards.Shown(En, Of($$"""{"cards": [], {{rules}}, "used": {"stripe_refund": {"minor": 20000, "unit": "EUR", "scale": 2} }, "active": true}"""), "Refunds"));
        Assert.Equal(("merged", "Refunds", "stripe_refund"), (merged.Id, merged.Title, merged.Rules[0].Action));
        Assert.Equal(20000, merged.Total!.Used!.Minor);
        Assert.Equal(0, Assert.Single(TafCards.Shown(En, Of($$"""{ {{rules}} }"""), "Refunds")).Total!.Used!.Minor);
        Assert.Null(Assert.Single(TafCards.Shown(En, Of($$"""{ {{rules}}, "usage_unknown": ["stripe_refund"]}"""), "Refunds")).Total!.Used);
        Assert.False(Assert.Single(TafCards.Shown(En, Of("""{"active": false}"""), "Refunds")).Active);
        // KIFF's Cards listed: those, as they are.
        Assert.Equal(TafCards.Read(En, Card("home.json")).Select(c => c.Id), TafCards.Shown(En, Card("home.json"), "Refunds").Select(c => c.Id));
    }

    static JsonElement Card(string file) =>
        ContractFile.Answer(SharedFiles.Contract(file)).GetProperty("tafs")[0].GetProperty("card");

    [Fact]
    public void Home_s_Card_reads_as_KIFF_says_it()
    {
        var card = Assert.Single(TafCards.Read(En, Card("home.json")));

        Assert.Equal(("card_rae", "Create refund", true, true), (card.Id, card.Title, card.Live, card.Active));
        Assert.Null(card.EnvironmentLabel);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), card.Expires);
        var rule = Assert.Single(card.Rules);
        Assert.Equal(("stripe_refund", "Create refund", Outside.Hold, 20), (rule.Action, rule.Label, rule.Outside, rule.CountPerWindow));
        Assert.Equal(KiffAmount.Euros(100), rule.PerAction);
        Assert.Equal(KiffAmount.Euros(1000), rule.PerWindow);
        Assert.Equal(("calendar_day", "a day", "today"), (rule.Window.Raw, rule.Window.Label, rule.Window.Current));
        Assert.Equal(KiffAmount.Euros(1000), card.Total?.Limit);
        Assert.Equal(KiffAmount.Euros(200), card.Total?.Used);
    }

    /// <summary>KIFF couldn't read its calls: their use is none, never zero, and the money total still leads.</summary>
    [Fact]
    public void Calls_whose_use_is_unknown_read_as_none()
    {
        var card = TafCards.Read(En, Card("home.json"))[0];

        Assert.Equal(20, card.Calls?.Limit);
        Assert.Null(card.Calls?.Used);
        Assert.Equal("€800", card.Hero(En)?.Figure);
    }

    [Fact]
    public void A_Card_that_can_t_be_read_is_left_out_and_a_missing_total_is_none()
    {
        var cards = TafCards.Read(En, JsonDocument.Parse("""
            {"cards": [{"title": "no id"}, {"id": "c", "live": false, "environment": "test", "total": {"limit": "lots"},
              "rules": [{"label": "no action"}, {"action": "a", "per_action": {"minor": 300, "unit": "", "scale": 2}}]}]}
            """).RootElement);

        var card = Assert.Single(cards);
        Assert.Equal("TEST", card.EnvironmentLabel);
        Assert.Null(card.Total);
        Assert.Null(card.Calls);
        var rule = Assert.Single(card.Rules);
        Assert.Equal("a", rule.Label);
        Assert.Equal(new KiffAmount(300, null, 0), rule.PerAction);
        Assert.Empty(TafCards.Read(En, JsonDocument.Parse("""{"cards": "none"}""").RootElement));
    }
}
