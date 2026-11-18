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

    /// <summary>How this is counted and the totals for each item apart: KIFF's and tafs cloud's words, as they are.</summary>
    [Fact]
    public void A_Card_carries_how_it_is_counted_and_its_totals_for_each_item()
    {
        var card = Assert.Single(TafCards.Read(En, JsonDocument.Parse("""
            {"cards": [{"id": "crd_1", "title": "Inbox", "rules": [],
              "counted": [{"tool": "gmail_list", "title": "List emails", "lines": ["Each list is one call.", " ", 3]},
                          {"tool": "gmail_read", "title": "", "lines": ["no title"]},
                          {"tool": "gmail_send", "title": "Send", "lines": []}],
              "each_item": ["1 call in any 7 days for each invoice id", "  "]},
             {"id": "crd_2", "title": "Plain", "rules": [], "counted": []}]}
            """).RootElement).Take(1));
        Assert.Equal("List emails", Assert.Single(card.Counted!).Title);
        Assert.Equal(["Each list is one call."], card.Counted![0].Lines);
        Assert.Equal(["1 call in any 7 days for each invoice id"], card.EachItem);
        var plain = TafCards.Read(En, JsonDocument.Parse("""{"cards": [{"id": "crd_2", "title": "Plain", "rules": [], "counted": []}]}""").RootElement)[0];
        Assert.Null(plain.Counted);
        Assert.Empty(plain.EachItem!);
    }

    /// <summary>
    /// Each sample taf's Card holds its template's rules, in the iPad's TafTemplates order, so its Card tab lists them;
    /// Support replies keeps its own Card. No sample's Card moves money.
    /// </summary>
    [Fact]
    public void Each_sample_taf_s_Card_holds_its_template_s_rules()
    {
        var cards = SampleTafs.All(En).ToDictionary(t => t.Key, t => t.Card);
        Assert.Equal(["deploy", "rollback", "migration", "destroy_data"], cards["deploys"].Rules.Select(r => r.Action));
        Assert.Equal(["delay_message", "reship", "change_address", "parcel_refund"], cards["parcels"].Rules.Select(r => r.Action));
        Assert.Equal(["reply_ticket", "promise", "share_customer_data"], cards["support"].Rules.Select(r => r.Action));
        Assert.Equal(["reply_lead", "offer_discount"], cards["leads"].Rules.Select(r => r.Action));
        Assert.Equal(["grant_role", "deactivate", "elevate", "grant_admin", "delete_user"], cards["access"].Rules.Select(r => r.Action));
        Assert.Equal(["deploys", "parcels", "support", "leads", "access"], cards.Keys);
        Assert.Equal("Reships", cards["parcels"].Rules[1].Label);
        Assert.True(cards["parcels"].Rules[1].AlwaysAsk);
        Assert.Equal(40, cards["support"].Rules[0].CountPerWindow);
        Assert.True(cards["deploys"].Rules[3].Never);
        Assert.All(cards.Values, c => Assert.NotEmpty(c.Rules));
        Assert.All(cards.Values.SelectMany(c => c.Rules), r => Assert.True(r.PerAction is null && r.PerWindow is null, r.Action));
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
