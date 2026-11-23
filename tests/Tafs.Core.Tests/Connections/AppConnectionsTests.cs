using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Record;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Connections;

/// <summary>
/// An app's connections in Connect apps, as the iPad's AppConnectionsView and its model, and Android's opensConnections.
/// </summary>
[Part("connections")]
public class AppConnectionsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static PlanqueGrant G(string id, string status, string agent = "ag_rae", string entry = "stripe", bool? signedIn = null, string kind = "oauth") =>
        new(id, status, "", "", new GrantRequest("stripe", agent, [], Kind: kind), []) { Entry = entry, SignedIn = signedIn, VendorName = "Stripe" };

    static readonly ConnectApp Stripe = new("stripe") { Vendor = "stripe", Title = "Stripe" };

    /// <summary>A sign-in kept after its card was declined (listed) opens the app's connections, so it can be ended.</summary>
    [Fact]
    public void A_declined_sign_in_s_app_opens_its_connections()
    {
        var listed = G("g1", "listed", signedIn: true);
        Assert.False(listed.Connected);
        Assert.False(Stripe.Connected);
        Assert.True(AppConnections.Opens(Stripe, [listed]));
        Assert.Equal("Remove access", AppConnections.EndWord(En, listed));
        // Nothing on this PC: nothing opens; a connected app always does.
        Assert.False(AppConnections.Opens(Stripe, []));
        Assert.True(AppConnections.Opens(Stripe with { State = "connected" }, []));
        // Ended ones aren't the app's any more.
        Assert.False(AppConnections.Opens(Stripe, [G("g2", "disconnected"), G("g3", "failed")]));
    }

    /// <summary>tafs cloud's removed tafs and an app's grant, read leniently: a removed taf without its agent is left out.</summary>
    [Fact]
    public void The_list_reads_removed_tafs_and_each_app_s_grant()
    {
        var list = ConnectAppsScreen.Read("""
            {"apps":[{"entry":"stripe@3","state":"connected","grant":"g-stripe"}],
             "removed_tafs":[{"agent":"old","removed_at":"2026-11-07T20:00:00Z","grants":[{"grant":"g1","used_by":[{"taf":"t1","name":"Chargebacks"}]},{"no":"grant"}]},
                             {"agent":"older"},{"removed_at":"x"}]}
            """);
        Assert.Equal("g-stripe", list.Apps[0].Grant);
        Assert.Equal(["old", "older"], list.RemovedTafs.Select(t => t.Agent));
        Assert.Equal("Chargebacks", Assert.Single(Assert.Single(list.RemovedTafs[0].Grants!).UsedBy).Name);
        Assert.Null(list.RemovedTafs[1].Grants);
        Assert.Empty(ConnectAppsScreen.Read("""{"apps":[]}""").RemovedTafs);
    }

    [Fact]
    public void An_app_s_connections_are_by_entry_or_the_grant_tafs_cloud_names()
    {
        var other = G("g9", "connected", entry: "gmail");
        Assert.Empty(AppConnections.Of(Stripe, [other]));
        Assert.Equal(["g9"], AppConnections.Of(Stripe with { Grant = "g9" }, [other]).Select(g => g.Id));
    }

    [Fact]
    public void Each_one_is_ended_in_its_own_words()
    {
        Assert.Equal("Cancel", AppConnections.EndWord(En, G("g", "approved")));
        Assert.Equal("Cancel the sign-in to Stripe?", AppConnections.CancelTitle(En, G("g", "approved")));
        Assert.Equal("Cancel the request", AppConnections.EndWord(En, G("g", "pending")));
        Assert.Equal("Cancel the request for Stripe?", AppConnections.CancelTitle(En, G("g", "pending")));
        Assert.Equal("Remove access", AppConnections.EndWord(En, G("g", "connected")));
        Assert.False(AppConnections.Cancellable(G("g", "connected")));
    }

    /// <summary>By taf, those needing attention first then by name; removed only on tafs cloud's word; the rest last.</summary>
    [Fact]
    public void Connections_group_by_taf_with_removed_only_on_tafs_cloud_s_word()
    {
        var grants = new[]
        {
            G("a", "connected", "ag_zed"), G("b", "connected", "ag_ana"), G("c", "approved", "ag_zed2"), G("d", "connected", "ag_gone"),
            G("e", "connected", "ag_unknown"), G("f", "connected", "ag_ana"),
        };
        var names = new Dictionary<string, string> { ["ag_zed"] = "Zed", ["ag_ana"] = "Ana", ["ag_zed2"] = "Zoe" };
        var (tafs, removed, others) = AppConnections.Groups(grants, g => names.GetValueOrDefault(g.Request.Agent), ["ag_gone"]);
        Assert.Equal(["Zoe", "Ana", "Zed"], tafs.Select(t => t.Taf));
        Assert.Equal(["b", "f"], tafs[1].Grants.Select(g => g.Id));
        Assert.Equal(["d"], removed.Select(g => g.Id));
        Assert.Equal(["e"], others.Select(g => g.Id));
    }

    /// <summary>Clear ends only what tafs cloud lists as a removed taf's, and keeps one a live taf's Card uses.</summary>
    [Fact]
    public void Clear_ends_only_what_tafs_cloud_lists_and_keeps_what_a_card_uses()
    {
        var removed = new[]
        {
            new RemovedTaf("ag_gone", [new RemovedGrant("d", []), new RemovedGrant("k", [new RemovedUsedBy("t1", "Chargebacks")])]),
            new RemovedTaf("ag_old", null),
        };
        var listed = AppConnections.RemovedListed(removed);
        var plan = AppConnections.ClearPlan(["d", "k", "x"], [G("d", "connected"), G("k", "connected"), G("x", "connected")], listed);
        Assert.Equal(["d"], plan.Ending.Select(g => g.Id));
        Assert.Equal("Chargebacks", Assert.Single(Assert.Single(plan.KeptFor)).Name);
        Assert.Equal("Cleared 1. Kept 1 for Chargebacks, whose Card uses it. 1 couldn't be cleared. Try again.",
            AppConnections.Cleared(En, 1, 1, ["Chargebacks"], 1));
        Assert.Equal("Kept: Chargebacks' Card uses it", AppConnections.KeptBy(En, ["Chargebacks", "Chargebacks", " "]));
        Assert.Null(AppConnections.KeptBy(En, []));
    }

    [Fact]
    public void The_question_names_the_taf_it_ends_and_who_else_stops()
    {
        Assert.Equal("Remove access to Stripe for Rae?", AppConnections.DisconnectTitle(En, "Stripe", "Rae", []));
        Assert.Equal("Remove access to Stripe? Ana and Bo stop too.", AppConnections.DisconnectTitle(En, "Stripe", null, ["Ana", "Bo"]));
        var message = AppConnections.DisconnectMessage(En, "Stripe", null, [], key: true);
        Assert.StartsWith("No taf uses Stripe now.", message);
        Assert.Contains("The key is deleted from tafs", message);
        Assert.Contains("revoked at Stripe", AppConnections.DisconnectMessage(En, "Stripe", "Rae", ["Ana"], key: false));
        Assert.Equal("/v1/accounts/t%2F1/grants/g%201", AppConnections.DisconnectPath("t/1", "g 1"));
    }

    /// <summary>Windows Hello first; closed, nothing is sent; each refusal counted; a sign-out stops what's left.</summary>
    [Fact]
    public async Task Ending_asks_Windows_Hello_first_then_planque_for_each()
    {
        var sent = new List<string>();
        Task<int> Delete(string id) { sent.Add(id); return Task.FromResult(id == "bad" ? 503 : 200); }

        Assert.Equal((0, (string?)null, false), await AppConnections.EndAsync(En, ["a", "b"], "Clear?", _ => Task.FromResult(StepConfirmed.Yes), Delete, () => true));
        Assert.Equal(["a", "b"], sent);

        sent.Clear();
        var (left, why, stopped) = await AppConnections.EndAsync(En, ["a"], "Clear?", _ => Task.FromResult(StepConfirmed.Cancelled), Delete, () => true);
        Assert.Equal((1, En.Get("common.not_done_unconfirmed"), false), (left, why, stopped));
        Assert.Empty(sent);

        (left, why, _) = await AppConnections.EndAsync(En, ["a", "bad"], "Clear?", _ => Task.FromResult(StepConfirmed.Yes), Delete, () => true);
        Assert.Equal(1, left);
        Assert.StartsWith("Access not removed.", why);

        sent.Clear();
        var current = true;
        Task<int> SignOut(string id) { sent.Add(id); current = false; return Task.FromResult(200); }
        Assert.True((await AppConnections.EndAsync(En, ["a", "b"], "Clear?", _ => Task.FromResult(StepConfirmed.Yes), SignOut, () => current)).Stopped);
        Assert.Equal(["a"], sent);
    }

    [Fact]
    public void A_connection_s_page_shows_planque_s_known_actions_in_its_order()
    {
        PlanqueGrant With(IReadOnlyList<GrantAction>? actions) =>
            new("g", "connected", "t", "d", new GrantRequest("gmail", "rae", []), []) { Actions = actions };
        Assert.False(AppConnections.RowFromPlanque(With(null)));
        Assert.True(AppConnections.RowFromPlanque(With([])));
        var g = With([
            new GrantAction("remove", "Remove access", "delete", "Rae stops reading mail."),
            new GrantAction("sign_in_again", "Sign in again", "approve", null),
            new GrantAction("sign_in_again", "Give a new key", "key", null),
            new GrantAction("rename", "Rename", "patch", null),
            new GrantAction("remove", "Remove", "patch", null),
            new GrantAction("remove_unfinished", "Remove the unfinished sign-in", "delete", null),
            new GrantAction("", "", "", null),
        ]);
        Assert.Equal(["remove", "sign_in_again", "sign_in_again", "remove_unfinished"], AppConnections.KnownActions(g).Select(a => a.Id));
        Assert.Equal([true, false, false, true], AppConnections.KnownActions(g).Select(AppConnections.Removes));
        Assert.Empty(AppConnections.KnownActions(With(null)));
    }

    [Fact]
    public void Also_on_names_the_other_tafs_whose_Card_draws_on_a_connection_s_tools()
    {
        Tafs.Core.Home.HomeTaf Taf(string name, string agent, IReadOnlyList<string> draws, string state = "running", bool revoked = false) =>
            new($"t_{name}", name, 0, state, "", null, [], 0, null, null, Agent: agent, Revoked: revoked, Draws: draws);
        var grant = new PlanqueGrant("g", "connected", "t", "d", new GrantRequest("stripe", "rae", [new GrantTool("refund"), new GrantTool("read", SetupReads: true)]), []);
        var tafs = new[]
        {
            Taf("Rae", "rae", ["refund"]),                 // the taf it was made for: left out
            Taf("Ana", "ana", ["refund", "email"]),        // draws on refund
            Taf("Bo", "bo", ["read"]),                      // only Setup's read, not a taf's tool
            Taf("Cy", "cy", ["refund"], revoked: true),     // its Card revoked
            Taf("Di", "di", ["refund"], state: "revoked"),
            Taf("Ed", "ed", ["refund"]),
        };
        Assert.Equal(["Ana", "Ed"], AppConnections.AlsoStops(grant, tafs));
        // planque's own tools win over the request's.
        Assert.Equal(["Bo"], AppConnections.AlsoStops(grant with { Tools = ["read"] }, tafs));
        Assert.Empty(AppConnections.AlsoStops(grant with { Tools = [] }, tafs));
        Assert.Null(AppConnections.AlsoOn(En, []));
        Assert.Equal("Also on Ana's Card", AppConnections.AlsoOn(En, ["Ana"]));
        Assert.StartsWith("Also on the Cards of Ana", AppConnections.AlsoOn(En, ["Ana", "Ed"]));
    }

    [Fact]
    public void Home_reads_what_a_taf_s_Card_draws_on()
    {
        var home = Tafs.Core.Home.HomeList.Read(En, System.Text.Json.JsonDocument.Parse("""
            {"tafs":[{"id":"t1","name":"Ana","state":"running","kiff_agent":"ana",
              "card":{"ids":["c1"],"active":true,"tools":["refund","email"],"rules":[{"action":"refund"},{"action":"charge"},{"x":1}]}},
             {"id":"t2","name":"Bo","state":"running"}]}
            """).RootElement, TimeZoneInfo.Utc);
        Assert.Equal(["refund", "email", "charge"], home.Tafs[0].CardDraws);
        Assert.Empty(home.Tafs[1].CardDraws);
    }

    /// <summary>The iPad's lastFailure: the newest not-done line on one of the connection's tools, the taf's own first.</summary>
    [Fact]
    public void A_connection_s_last_failed_call_is_its_own_taf_s_newest_on_its_tools()
    {
        var g = new PlanqueGrant("grt_s", "connected", "", "d", new GrantRequest("stripe", "agt_refunds",
            [new GrantTool("stripe__refund"), new GrantTool("stripe__read", SetupReads: true)]), []);
        RecordEntry Line(string action, string agent, EntryKind kind, int minutes, string title) =>
            new("T", 0, kind, title, "Stripe refused", DateTimeOffset.Parse("2026-11-23T08:00:00Z").AddMinutes(minutes),
                Call: new ActivityCall("p", action, "allowed", DateTimeOffset.Parse("2026-11-23T08:00:00Z"), false, ActorId: agent));
        var record = new[]
        {
            Line("stripe__refund", "agt_other", EntryKind.NotDone, 30, "Another taf's, newer"),
            Line("stripe__refund", "agt_refunds", EntryKind.NotDone, 10, "Its own"),
            Line("stripe__refund", "agt_refunds", EntryKind.Done, 40, "Done, not failed"),
            Line("stripe__read", "agt_refunds", EntryKind.NotDone, 50, "A setup read, not a tool it uses"),
        };
        Assert.Equal("Last failed: Its own · Stripe refused", AppConnections.LastFailure(En, g, record));
        // With none of its own, another taf's on the same tools.
        Assert.Equal("Last failed: Another taf's, newer · Stripe refused", AppConnections.LastFailure(En, g, record.Where(l => l.Title != "Its own").ToList()));
        // planque's own list of tools wins over the request's.
        Assert.Null(AppConnections.LastFailure(En, g with { Tools = ["stripe__list"] }, record));
        Assert.Null(AppConnections.LastFailure(En, g, []));
    }

    /// <summary>A browser connection's page says the Chrome it runs in, by its name; moving it isn't offered (the phone's key signs it).</summary>
    [Fact]
    public void A_browser_connection_says_which_Chrome_holds_it_and_offers_no_move()
    {
        var plain = new PlanqueGrant("g", "connected", "t", "d", new GrantRequest("wiki", "rae", []), []);
        Assert.Null(AppConnections.HeldIn(En, plain));
        var held = plain with { Chrome = new ChromeConnection("c1", " Work laptop ", ["wikipedia.org"]) };
        Assert.Equal("In Work laptop", AppConnections.HeldIn(En, held));
        Assert.Null(AppConnections.HeldIn(En, plain with { Chrome = new ChromeConnection("c1", "  ", []) }));
        Assert.Null(AppConnections.HeldIn(En, plain with { Chrome = new ChromeConnection("c1", null, []) }));
        var move = held with { Actions = [new GrantAction("use_another_chrome", "Use another Chrome", "approve", null)] };
        Assert.Empty(AppConnections.KnownActions(move));
    }
}
