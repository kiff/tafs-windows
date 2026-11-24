using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Usage;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Usage;

/// <summary>KIFF's usage on Plan &amp; usage: who bills, asking an admin, members' requests, and the signed decline.</summary>
[Part("calendar")]
public class KiffUsageTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static KiffUsage Read(string plan, string requests = "[]") => KiffUsage.Read(JsonDocument.Parse($$"""
        {"plan": "{{plan}}", "billing": {"source": "stripe"},
         "store": {"solo_product": "solo", "packs": [{"id": "first", "name": "First month", "line": "First month: 500 credits", "apple_product": "p0", "once": true},
                                                      {"id": "rayo", "name": "Rayo", "line": "Rayo: 2,000 credits", "apple_product": "p1"}]},
         "requests": {{requests}}}
        """).RootElement)!;

    [Fact]
    public void Who_bills_and_what_a_member_may_ask()
    {
        var team = Read("team");
        Assert.Equal("Billed by KIFF", team.BillingLine(En));
        Assert.True(team.Asks);
        Assert.False(Read("solo").Asks);
        Assert.Null(Read("internal").Asks);
        Assert.Equal(["rayo"], Read("solo").Askable.Select(p => p.Id));
        Assert.Null(KiffUsage.Read(JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void A_member_sees_their_own_request_and_an_admin_sees_every_open_one()
    {
        var u = Read("solo", """[{"id": "q1", "asked_by": "s1", "kind": "pack", "pack": "rayo", "reason": "Busy week", "status": "open", "created_at": "2026-11-16T09:00:00Z", "expires_at": "2026-11-23T09:00:00Z"}]""");
        Assert.Equal("q1", u.Waiting(managesBilling: false)!.Id);
        Assert.Null(u.Waiting(managesBilling: true));
        Assert.Equal(["q1"], u.Open(managesBilling: true).Select(r => r.Id));
        Assert.Empty(u.Open(managesBilling: false));
        Assert.Contains("Rayo: 2,000 credits", u.RequestLine(En, u.Requests[0]));
        var declined = Read("team", """[{"id": "q2", "kind": "raise", "status": "declined", "note": "Next month"}]""");
        Assert.Contains("Next month", declined.LastAnswer(En, managesBilling: false));
        Assert.Null(declined.LastAnswer(En, managesBilling: true));
    }

    [Fact]
    public void The_ask_and_its_routes()
    {
        Assert.Equal("""{"reason":"Busy week","kind":"raise"}""", Encoding.UTF8.GetString(KiffUsage.AskBody(true, null, " Busy week ")));
        Assert.Equal("""{"reason":"","pack":"rayo"}""", Encoding.UTF8.GetString(KiffUsage.AskBody(false, "rayo", "")));
        Assert.Equal(("/v1/me/pack-requests/q%2F1/withdraw", "/v1/me/pack-requests/q1/decline"), (KiffUsage.WithdrawPath("q/1"), KiffUsage.DeclinePath("q1")));
    }

    /// <summary>
    /// Where to buy on a PC (no store here): where tafs cloud's balance sells packs or Solo (`buy`, as the iPad's BalancePanel),
    /// the phone's app, and Team on kiff.dev unless the plan is Team; nothing where it sells nothing (a Company admin raises
    /// the limit). An offer is `true` or an object; `false`, `null` or none isn't one.
    /// </summary>
    [Theory]
    [InlineData("""{"packs": true, "solo": true}""", "solo", true, true)]
    [InlineData("""{"packs": false, "solo": {"first_month": {"ready": true}}}""", "free", true, true)]
    [InlineData("""{"packs": true}""", "team", true, false)]
    [InlineData("""{"packs": false, "solo": null}""", "solo", false, false)]
    [InlineData("null", "team", false, false)]
    [InlineData("", "solo", false, false)]
    public void A_pc_says_where_to_buy_where_tafs_cloud_sells(string buy, string plan, bool onPhone, bool teamOnWeb)
    {
        var json = "{\"balance\": {\"words\": \"120 credits left\"" + (buy.Length > 0 ? ", \"buy\": " + buy : "") + "}}";
        var b = Tafs.Core.Usage.UsageScreen.ReadBalance(json)!;
        Assert.Equal((onPhone, teamOnWeb), b.WhereToBuy(plan));
        Assert.Equal("https://kiff.dev/dashboard/checkout", Tafs.Core.Usage.Balance.TeamCheckout.AbsoluteUri);
    }
}
