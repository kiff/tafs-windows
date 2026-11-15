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
