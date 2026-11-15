using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Approvals;

/// <summary>KIFF's drafts as the person reads them before confirming, as the iPad's AgentDraft and DraftCard.</summary>
[Part("approvals")]
public class KiffDraftsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static AgentDraft? Read(string json) => KiffDrafts.Read(JsonDocument.Parse(json).RootElement);

    static AgentDraft Draft(string cards, string more = "") =>
        Read($$"""{"id": "d_01", "agent_id": "agt_refunds", "status": "draft", "version": "v3", "label": " Refunds assistant ", "cards": [{{cards}}]{{more}}}""")!;

    /// <summary>Every field the iPad reads, in its shape; the claim secret is checked but never kept.</summary>
    [Fact]
    public void A_draft_is_read_whole()
    {
        var d = Draft("""{"grants": [{"action": "stripe__create_refund", "title": "Refund a payment", "per_action_max": [{"quantity": "sum(amount)", "max": 10000, "unit": "EUR", "scale": 2}], "resources": {"parameter": "payment", "values": ["pi_1"]}, "entity_states": ["open"]}], "aggregates": [{"quantity": "count", "limit": 20, "window": "calendar_day"}], "on_exceed": "owner", "revises": "c_1", "unchanged": false, "current": {"grants": [], "lines": ["Old"]}, "new_field": 1}""",
            """, "claim_secret": "secret", "card_ids": ["c_1"], "no_tools": false, "reason": null, "retires": [{"id": "c_0", "current": {"aggregates": []}}], "from_hold": "h_1" """);

        Assert.Equal(("d_01", "agt_refunds", "draft", "v3", "h_1"), (d.Id, d.AgentId, d.Status, d.Version, d.FromHold));
        var card = Assert.Single(d.Cards!);
        Assert.Equal(new DraftCap("sum(amount)", 10000, "EUR", 2), card.Grants![0].PerActionMax![0]);
        Assert.Equal(("c_1", false, true), (card.Revises, card.IsUnchanged, card.AsksPastLimits));
        Assert.Equal(["Old"], card.Current!.KiffLines!);
        Assert.Equal("c_0", d.Retires![0].Id);
        Assert.DoesNotContain("secret", d.ToString());
        Assert.False(d.Signed);
        Assert.True((d with { Status = "claimed" }).Signed);
    }

    /// <summary>As the iPad's synthesized decoder: a required field missing, or one of another shape, fails the draft.</summary>
    [Theory]
    [InlineData("""{"agent_id": "a", "status": "draft", "version": "v"}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": 3}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{"grants": [{"title": "x"}]}]}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{"grants": [{"action": "a", "per_action_max": [{"quantity": "count", "max": 1.5}]}]}]}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{"aggregates": [{"quantity": "count", "limit": 1}]}]}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{"lines": "x"}]}""")]
    [InlineData("""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "no_tools": "yes"}""")]
    public void A_draft_it_can_t_read_isn_t_shown(string json) => Assert.Null(Read(json));

    /// <summary>
    /// As the iPad's DraftCard.lines: each action by its title, with its cap on each call in KIFF's unit; the totals in KIFF's
    /// window; on a Card that asks past its limits, what asks you; KIFF's own lines win when sent.
    /// </summary>
    [Fact]
    public void A_Card_reads_as_its_lines()
    {
        var asks = Draft("""{"on_exceed": "owner", "grants": [{"action": "stripe__create_refund", "title": "Refund a payment", "per_action_max": [{"quantity": "sum(amount)", "max": 10000, "unit": "EUR", "scale": 2}]}, {"action": "stripe__list_charges"}, {"action": "send", "per_action_max": [{"quantity": "count", "max": 3}]}], "aggregates": [{"quantity": "sum(amount)", "limit": 100000, "window": "calendar_day", "unit": "EUR", "scale": 2}, {"quantity": "count", "limit": 20, "window": "calendar_day"}, {"quantity": "count", "limit": 0, "window": "calendar_day"}, {"quantity": "sum(amount)", "limit": 30000, "window": "calendar_day"}]}""");

        Assert.Equal(["Refund a payment: up to €100 each; more asks you first", "list charges", "send: up to 3 each; more asks you first",
            "€1,000 in total a day, then it asks you", "20 calls a day, then it asks you", "Each call asks you first",
            "30,000 (no currency) in total a day, then it asks you"], asks.CardLines(En)[0]);

        var refuses = Draft("""{"on_exceed": "refuse", "aggregates": [{"quantity": "count", "limit": 1, "window": "calendar_day"}, {"quantity": "count", "limit": 0, "window": "calendar_day"}]}""");
        Assert.Equal(["1 call a day", "0 calls a day"], refuses.CardLines(En)[0]);

        var kiff = Draft("""{"lines": ["KIFF's own line"], "grants": [{"action": "a"}]}""");
        Assert.Equal(["KIFF's own line"], kiff.CardLines(En)[0]);
    }

    /// <summary>Two grants that read alike, on one Card or two, each add their action in words; one alone stays as it is.</summary>
    [Fact]
    public void Grants_that_read_alike_add_their_action()
    {
        var d = Draft("""{"grants": [{"action": "stripe__create_refund_repg", "title": "Write Stripe API data"}, {"action": "stripe__list"}]}, {"grants": [{"action": "stripe__update_customer", "title": "write stripe api data"}]}""");

        Assert.Equal([["Write Stripe API data · create refund repg", "list"], ["write stripe api data · update customer"]], d.CardLines(En));
    }

    /// <summary>
    /// A scale past 18 would overflow the figure: a draft with one, on a cap or a total, isn't shown, never a crash; up to 18
    /// is written. The Card tab's amounts, made the same way, read "–".
    /// </summary>
    [Theory]
    [InlineData(19)]
    [InlineData(29)]
    [InlineData(64)]
    public void A_scale_past_18_is_never_written(int scale)
    {
        string Cap(int s) => $$"""{"grants": [{"action": "a", "per_action_max": [{"quantity": "sum(amount)", "max": 5, "unit": "EUR", "scale": {{s}}}]}]}""";
        string Total(int s) => $$"""{"aggregates": [{"quantity": "sum(amount)", "limit": 5, "window": "calendar_day", "unit": "EUR", "scale": {{s}}}]}""";
        Assert.Null(Read($$"""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{{Cap(scale)}}]}"""));
        Assert.Null(Read($$"""{"id": "d", "agent_id": "a", "status": "draft", "version": "v", "cards": [{{Total(scale)}}]}"""));
        Assert.Equal(["a: up to €0.000000000000000005 each"], Draft(Cap(18)).CardLines(En)[0]);
        Assert.Equal("–", new Tafs.Core.Taf.KiffAmount(5, "EUR", scale).Figure(En));
    }

    /// <summary>As the iPad's cardTitle: the name it was submitted with, never its agent id.</summary>
    [Fact]
    public void The_Card_is_named_after_its_taf()
    {
        Assert.Equal("Refunds assistant's Card", Draft("").CardTitle(En));
        Assert.Equal("The taf's Card", (Draft("") with { Label = "  " }).CardTitle(En));
        Assert.Equal("/v1/me/agent-drafts/d%2F1", KiffDrafts.Path("d/1"));
    }
}
