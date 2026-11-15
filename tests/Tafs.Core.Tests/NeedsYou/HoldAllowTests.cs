using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// "Allow this from now on" on a held call, as the iPad's AllowFromNowOnTests: the app's own rule for the button, tafs
/// cloud's offer made on the tap, and a signature only over KIFF's draft as this PC read it, when it is what was offered.
/// </summary>
[Part("approvals")]
public class HoldAllowTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Cap = """{"quantity":"amount","max":15000,"unit":"EUR","scale":2}""";
    const string Raised = """{"quantity":"amount","max":32000,"unit":"EUR","scale":2}""";
    const string Day = """{"quantity":"amount","limit":50000,"window":"day","unit":"EUR","scale":2}""";
    const string Agent = "\"agent_id\":\"refunds-1\",";

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>A Card's <c>"grants":[…]</c>.</summary>
    static string Grants(params string[] grants) => "\"grants\":[" + string.Join(",", grants) + "]";

    /// <summary>A Card's <c>,"aggregates":[…]</c>, to follow its grants.</summary>
    static string Totals(params string[] totals) => ",\"aggregates\":[" + string.Join(",", totals) + "]";

    static string Refund(params string[] caps) => "{\"action\":\"create_refund\",\"per_action_max\":[" + string.Join(",", caps) + "]}";

    /// <summary>KIFF's draft: Card c1 revised to <paramref name="after"/>, with <paramref name="current"/> its terms in force, and anything <paramref name="extra"/> adds.</summary>
    static string Card(string after, string current, string extra = "", string retires = "", string from = "", string agent = Agent) =>
        "{\"id\":\"dr_9\"," + agent + "\"status\":\"draft\",\"version\":\"v7\",\"cards\":[{\"revises\":\"c1\"," + after + ",\"current\":{" + current + "}}"
        + extra + "]" + retires + from + "}";

    static string Draft(int max = 32000, string extra = "", string retires = "") =>
        Card(Grants(Refund(Cap.Replace("15000", max.ToString()))), Grants(Refund(Cap)), extra, retires);

    static string OfferJson(string kind = "per_action", string before = "15000", string after = "32000", string unit = "\"unit\":\"EUR\",", string window = "") =>
        "{\"offer\":{\"changes\":[{\"mandate_id\":\"c1\",\"kind\":\"" + kind + "\",\"before\":" + before + ",\"after\":" + after + "," + unit + window
        + "\"words_before\":\"Refunds up to €150 each\",\"words_after\":\"Refunds up to €320 each\",\"rounded\":false}],"
        + "\"would\":{\"calls\":4,\"words\":\"With this Card, 4 more refunds would have gone out on their own (€1,180)\"},"
        + "\"draft\":\"dr_9\",\"confirm_key\":\"critical\",\"send_held\":true,\"round\":{\"after\":35000,\"words\":\"€350\"}}}";

    static AllowOffer Offer(string? json = null)
    {
        var answer = AllowOffers.Read(Json(json ?? OfferJson()));
        Assert.NotNull(answer);
        Assert.NotNull(answer.Offer);
        return answer.Offer;
    }

    static bool Signs(string draft, string? offerJson = null, string? agent = "refunds-1", string? hold = "ce_9")
    {
        var d = KiffDrafts.Read(Json(draft));
        Assert.NotNull(d);
        return HoldAllow.Matches(d, Offer(offerJson), agent, hold, "create_refund");
    }

    static HeldCall Held(IReadOnlyList<string>? outcomes, string? kiffId = "ce_9") =>
        new("Refunds", 0, "Refund a payment", null, "", "Over", DateTimeOffset.UtcNow.AddMinutes(10), [])
        {
            KiffId = kiffId, Action = "create_refund", Agent = "refunds-1", AccountingVersion = 3, BreachOutcomes = outcomes,
        };

    // The app's own rule for the button

    /// <summary>The iPad's testTheButtonShowsOnAHoldACardChangeCanLift.</summary>
    [Fact]
    public void The_button_shows_on_a_hold_a_Card_change_can_lift()
    {
        Assert.True(HoldAllow.Allowable(Held(["per_action_exceeded"])));
        Assert.True(HoldAllow.Allowable(Held(["limit_reached", "resource_not_on_card"])));
        Assert.False(HoldAllow.Allowable(Held(["per_action_exceeded", "action_not_on_card"])), "one breach no Card change lifts");
        Assert.False(HoldAllow.Allowable(Held(["something_new"])), "a breach this app doesn't know");
        Assert.False(HoldAllow.Allowable(Held([])), "no breach");
        Assert.False(HoldAllow.Allowable(Held(null)), "a sample, or a call saved before its breaches were kept");
        Assert.False(HoldAllow.Allowable(Held(["per_action_exceeded"], kiffId: null)), "not KIFF's");
    }

    /// <summary>
    /// The iPad's testTheButtonWaitsForTheServiceToSayItIsLive: <c>holds.allow</c> among the account's capabilities. Unlike the
    /// older capabilities, no list means no button.
    /// </summary>
    [Fact]
    public void The_button_waits_for_the_service_to_say_it_is_live()
    {
        Assert.True(HoldAllow.AllowIsLive(["holds.answer", "cards.manage", "holds.allow"]));
        Assert.False(HoldAllow.AllowIsLive(["holds.answer", "cards.manage"]), "not live yet");
        Assert.False(HoldAllow.AllowIsLive([]));
        Assert.False(HoldAllow.AllowIsLive(null), "an account that lists no capabilities");
    }

    /// <summary>The iPad's testAHeldCallKeepsItsAgentAndItsBreaches: the held call KIFF sends carries what the rule reads.</summary>
    [Fact]
    public void A_held_call_keeps_its_agent_and_its_breaches()
    {
        var e = KiffHeld.Hold(Json("""
            {"id":"ce_9","subject":"refunds-1","action":"create_refund","entity_id":"ch_1","status":"held","breaches":[{"mandate_id":"c1","outcome":"per_action_exceeded","quantity":"amount","limit":15000}],"parameters":{"amount":32000},"created_at":"2026-11-12T09:00:00Z","expires_at":"2099-01-01T00:00:00Z","accounting_version":3}
            """));
        Assert.NotNull(e);
        var call = KiffHeld.ToHeldCall(En, e, "Refunds", 0);
        Assert.Equal("refunds-1", call.Agent);
        Assert.Equal("create_refund", call.Action);
        Assert.Equal(["per_action_exceeded"], call.BreachOutcomes!);
        Assert.True(HoldAllow.Allowable(call));
    }

    // This PC signs what the person saw

    /// <summary>The iPad's testADraftThatCarriesExactlyTheOfferedChangeMatches.</summary>
    [Fact]
    public void A_draft_that_carries_exactly_the_offered_change_matches() => Assert.True(Signs(Draft()));

    /// <summary>The iPad's testAnotherFigureAnotherCardARetiredCardOrANewCardIsNotWhatWasOffered.</summary>
    [Fact]
    public void Another_figure_another_Card_a_retired_Card_or_a_new_Card_is_not_what_was_offered()
    {
        Assert.False(Signs(Draft(max: 50000)), "another figure");
        const string other = """,{"revises":"c2","grants":[{"action":"list_charges"}],"current":{"grants":[{"action":"list_charges"},{"action":"create_refund"}]}}""";
        Assert.False(Signs(Draft(extra: other)), "another Card changed");
        Assert.False(Signs(Draft(retires: ""","retires":[{"id":"c3"}]""")), "a Card retired");
        Assert.False(Signs(Draft(extra: """,{"grants":[{"action":"create_refund"}]}""")), "a Card that revises none in force");
        const string unchanged = """,{"revises":"c2","unchanged":true,"current":{"grants":[{"action":"list_charges"}]},"grants":[{"action":"list_charges"}]}""";
        Assert.True(Signs(Draft(extra: unchanged)), "a Card left as it is doesn't count");
        var twice = """,{"revises":"c1","grants":[""" + Refund(Raised) + """],"current":{"grants":[""" + Refund(Cap) + "]}}";
        Assert.False(Signs(Draft(extra: twice)), "the named Card twice");
    }

    /// <summary>The iPad's testAChangeOfAKindTheContractDoesntNameIsNeverSigned.</summary>
    [Fact]
    public void A_change_of_a_kind_the_contract_doesnt_name_is_never_signed() =>
        Assert.False(Signs(Draft(), OfferJson(kind: "something_new")));

    /// <summary>The iPad's testTheChangedCardReadsAsTheCardInForceWithOnlyTheOfferedFigureMoved.</summary>
    [Fact]
    public void The_changed_Card_reads_as_the_Card_in_force_with_only_the_offered_figure_moved()
    {
        var count = Cap.Replace("amount", "count");
        Assert.True(Signs(Card(Grants(Refund(Raised)), Grants(Refund(Cap)))), "the offered cap, nothing else");
        Assert.False(Signs(Card(Grants(Refund(Raised, count)), Grants(Refund(Cap, count.Replace("15000", "3"))))), "a second cap moved too");
        Assert.False(Signs(Card(Grants(Refund(Raised, Cap.Replace("amount", "fee"))), Grants(Refund(Cap)))), "a cap added");
        Assert.False(Signs(Card(Grants(Refund()), Grants(Refund(Cap)))), "a cap taken away");
        Assert.False(Signs(Card(Grants(Refund(Raised), """{"action":"delete_customer"}"""), Grants(Refund(Cap)))), "an action added");
        Assert.False(Signs(Card(Grants(Refund(Raised)), Grants(Refund(Cap), """{"action":"list_charges"}"""))), "an action taken away");
        var payout = """{"action":"create_payout","per_action_max":[""" + Cap + "]}";
        Assert.False(Signs(Card(Grants(Refund(Cap), payout.Replace("15000", "32000")), Grants(Refund(Cap), payout))), "the same figures on another action's cap");
        Assert.False(Signs(Card(Grants(Refund(Raised)) + Totals(Day), Grants(Refund(Cap)))), "a total added");
        Assert.False(Signs(Card(Grants(Refund(Raised)), Grants(Refund(Cap)) + Totals(Day))), "a total dropped");
        Assert.False(Signs(Card(Grants(Refund(Raised)) + Totals(Day.Replace("day\"", "week\"")), Grants(Refund(Cap)) + Totals(Day))), "a total's window changed");
        Assert.False(Signs(Card(Grants(Refund(Raised)) + Totals(Day.Replace("50000", "90000")), Grants(Refund(Cap)) + Totals(Day))), "a total's figure changed");
        Assert.False(Signs(Card(Grants(Refund(Raised)) + ",\"on_exceed\":\"refuse\"", Grants(Refund(Cap)) + ",\"on_exceed\":\"owner\"")), "what happens past a limit changed");
        Assert.False(Signs(Card(Grants(Refund(Raised)), Grants(Refund(Cap)) + ",\"on_exceed\":\"owner\"")), "what happens past a limit dropped");
        Assert.True(Signs(Card(Grants(Refund(Raised).Replace("{\"action\"", "{\"title\":\"Refund\",\"action\"")) + ",\"lines\":[\"x\"]",
            Grants(Refund(Cap)) + ",\"lines\":[\"y\"]")), "KIFF's lines and a grant's title are words, not terms");
    }

    /// <summary>The iPad's testTheUnitMustBeTheOffersExactly.</summary>
    [Fact]
    public void The_unit_must_be_the_offer_s_exactly()
    {
        var (usdRaised, usdCap) = (Raised.Replace("EUR", "USD"), Cap.Replace("EUR", "USD"));
        Assert.False(Signs(Card(Grants(Refund(usdRaised)), Grants(Refund(usdCap)))), "another currency");
        Assert.False(Signs(Card(Grants(Refund(Raised)), Grants(Refund(Cap))), OfferJson(unit: "")), "an offer with no unit for a cap in euros");
        var (bareRaised, bareCap) = ("""{"quantity":"amount","max":32000}""", """{"quantity":"amount","max":15000}""");
        Assert.False(Signs(Card(Grants(Refund(bareRaised)), Grants(Refund(bareCap)))), "an offer in euros for a cap with no unit");
        Assert.True(Signs(Card(Grants(Refund(bareRaised)), Grants(Refund(bareCap))), OfferJson(unit: "")), "no unit on either");
    }

    /// <summary>The iPad's testATotalOrACountChangesOnlyItsFigureInItsWindow.</summary>
    [Fact]
    public void A_total_or_a_count_changes_only_its_figure_in_its_window()
    {
        var total = OfferJson(kind: "total", before: "50000", after: "60000");
        var now = Grants(Refund(Cap)) + Totals(Day);
        static string After(string d) => Grants(Refund(Cap)) + Totals(d);
        Assert.True(Signs(Card(After(Day.Replace("50000", "60000")), now), total));
        Assert.False(Signs(Card(After(Day.Replace("50000", "70000")), now), total), "another figure");
        Assert.False(Signs(Card(After(Day.Replace("50000", "60000").Replace("day\"", "week\"")), now), total), "the window moved with it");
        Assert.False(Signs(Card(Grants(Refund(Raised)) + Totals(Day.Replace("50000", "60000")), now), total), "the cap moved too");
        Assert.False(Signs(Card(After(Day.Replace("50000", "60000")), now), OfferJson(kind: "total", before: "50000", after: "60000", unit: "\"unit\":\"USD\",")),
            "another unit");

        // Two totals at the same figure, a day's and a month's: the offer's window says which one moves.
        var month = Day.Replace("day\"", "month\"");
        var both = Grants(Refund(Cap)) + Totals(Day, month);
        var dayMoved = Grants(Refund(Cap)) + Totals(Day.Replace("50000", "60000"), month);
        var monthMoved = Grants(Refund(Cap)) + Totals(Day, month.Replace("50000", "60000"));
        var daily = OfferJson(kind: "total", before: "50000", after: "60000", window: "\"window\":\"day\",");
        Assert.True(Signs(Card(dayMoved, both), daily));
        Assert.False(Signs(Card(monthMoved, both), daily), "the month's total moved, the day's was offered");
    }

    /// <summary>
    /// The iPad's testAResourceChangeAddsExactlyTheOfferedValue: exactly the values in force plus the offered one, on the
    /// held action's scope for the offered parameter.
    /// </summary>
    [Fact]
    public void A_resource_change_adds_exactly_the_offered_value()
    {
        static string Scoped(string values, string parameter = "account", string action = "create_refund") =>
            "\"grants\":[{\"action\":\"" + action + "\",\"per_action_max\":[" + Cap + "],\"resources\":{\"parameter\":\"" + parameter + "\",\"values\":[" + values + "]}}]";
        const string resource = """{"offer":{"changes":[{"mandate_id":"c1","kind":"resource","parameter":"account","value":"acct_2","words_before":"On acct_1","words_after":"On acct_1 and acct_2"}],"draft":"dr_9"}}""";
        var now = Scoped("\"acct_1\"");
        Assert.True(Signs(Card(Scoped("\"acct_1\",\"acct_2\""), now), resource));
        Assert.True(Signs(Card(Scoped("\"acct_2\",\"acct_1\""), now), resource), "the order of the values doesn't matter");
        Assert.False(Signs(Card(now, now), resource), "nothing added");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_2\",\"acct_3\""), now), resource), "a second value added");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_3\""), now), resource), "another value added");
        Assert.False(Signs(Card(Scoped("\"acct_2\""), now), resource), "a value in force dropped");
        Assert.False(Signs(Card(Grants(Refund(Cap)), now), resource), "the scope dropped: any value");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_2\"", parameter: "customer"), Scoped("\"acct_1\"", parameter: "customer")), resource), "another parameter's scope");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_2\"", action: "create_payout"), Scoped("\"acct_1\"", action: "create_payout")), resource), "another action's scope");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_2\"").Replace("15000", "32000"), now), resource), "a cap moved with it");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_2\""), now), resource.Replace("\"value\":\"acct_2\",", "")), "an offer that names no value");
        Assert.False(Signs(Card(Scoped("\"acct_1\",\"acct_1\""), Scoped("\"acct_1\"")), resource.Replace("acct_2", "acct_1")), "a value already in force");
    }

    /// <summary>
    /// The iPad's testEveryOtherTermMustBeTheCardInForces: each term that decides what a taf may do, one at a time. A draft
    /// that differs from the Card in force in that term, beside the offered cap, is never signed.
    /// </summary>
    [Fact]
    public void Every_other_term_must_be_the_Card_in_force_s()
    {
        static string Grant(string caps, string more = "") => "{\"action\":\"create_refund\",\"per_action_max\":[" + caps + "]" + more + "}";
        static void Differs(string after, string now, string what) => Assert.False(Signs(Card(after, now)), what);
        const string scope = ""","resources":{"parameter":"account","values":["acct_1"]}""";
        // The same terms on both sides, with only the cap moved: signed. Each case below starts from here.
        const string whole = ""","resources":{"parameter":"account","values":["acct_1"]},"entity_states":["paid"],"next_steps":["return_first"]""";
        const string totals = """
            ,"aggregates":[{"quantity":"count","limit":20,"window":"day","measure":"attempts","per":"invoice_id"}],"on_exceed":"owner","subject":"refunds-1","valid_until":"2027-01-01T00:00:00Z","domain":"Shop","environment":"live"
            """;
        Assert.True(Signs(Card(Grants(Grant(Raised, whole)) + totals, Grants(Grant(Cap, whole)) + totals + ",\"subjects\":[\"refunds-1\"]")),
            "every term the same, the cap moved");

        // A grant's scope.
        Differs(Grants(Grant(Raised)), Grants(Grant(Cap, scope)), "the scope dropped");
        Differs(Grants(Grant(Raised, scope.Replace("[\"acct_1\"]", "[\"acct_1\",\"acct_2\"]"))), Grants(Grant(Cap, scope)), "a value added to the scope");
        Differs(Grants(Grant(Raised, scope.Replace("account", "customer"))), Grants(Grant(Cap, scope)), "the scope's parameter changed");
        Differs(Grants(Grant(Raised, scope)), Grants(Grant(Cap)), "a scope added");
        // The states it applies in.
        Differs(Grants(Grant(Raised)), Grants(Grant(Cap, ",\"entity_states\":[\"paid\"]")), "a state restriction dropped");
        Differs(Grants(Grant(Raised, ",\"entity_states\":[\"paid\",\"refunded\"]")), Grants(Grant(Cap, ",\"entity_states\":[\"paid\"]")), "a state added");
        Assert.True(Signs(Card(Grants(Grant(Raised)), Grants(Grant(Cap, ",\"entity_states\":[]")))), "no states, written as none or as an empty list");
        Assert.True(Signs(Card(Grants(Grant(Raised)), Grants(Grant(Cap, ",\"entity_states\":null")))), "no states, written as null");
        // The other answers to a held call.
        Differs(Grants(Grant(Raised)), Grants(Grant(Cap, ",\"next_steps\":[\"return_first\"]")), "a next step dropped");
        Differs(Grants(Grant(Raised, ",\"next_steps\":[\"return_first\",\"proof_first\"]")), Grants(Grant(Cap, ",\"next_steps\":[\"return_first\"]")), "a next step added");
        // A total: per item or one total, and what it counts.
        const string perItem = """{"quantity":"count","limit":1,"window":"week","measure":"attempts","per":"invoice_id"}""";
        Differs(Grants(Grant(Raised)) + Totals(perItem.Replace(",\"per\":\"invoice_id\"", "")), Grants(Grant(Cap)) + Totals(perItem), "a per-item total turned into one total");
        Differs(Grants(Grant(Raised)) + Totals(perItem.Replace("invoice_id", "customer_id")), Grants(Grant(Cap)) + Totals(perItem), "a total kept per another parameter");
        Differs(Grants(Grant(Raised)) + Totals(perItem.Replace("attempts", "effects")), Grants(Grant(Cap)) + Totals(perItem), "what a total counts changed");
        // A measure a draft leaves out is KIFF's default: attempts for a count, effects for a sum.
        const string sum = """{"quantity":"sum(amount)","limit":50000,"window":"day"}""";
        Assert.True(Signs(Card(Grants(Grant(Raised)) + Totals(sum), Grants(Grant(Cap)) + Totals(sum.Replace("}", ",\"measure\":\"effects\"}")))),
            "a sum's measure left out reads as effects");
        Differs(Grants(Grant(Raised)) + Totals(sum), Grants(Grant(Cap)) + Totals(sum.Replace("}", ",\"measure\":\"attempts\"}")), "a sum counting attempts in force, left out in the draft");
        const string count = """{"quantity":"count","limit":20,"window":"day"}""";
        Assert.True(Signs(Card(Grants(Grant(Raised)) + Totals(count), Grants(Grant(Cap)) + Totals(count.Replace("}", ",\"measure\":\"attempts\"}")))),
            "a count's measure left out reads as attempts");
        Differs(Grants(Grant(Raised)) + Totals(count), Grants(Grant(Cap)) + Totals(count.Replace("}", ",\"measure\":\"effects\"}")), "a count counting effects in force, left out in the draft");
        // When it starts and ends.
        Differs(Grants(Grant(Raised)) + ",\"valid_until\":\"2028-01-01T00:00:00Z\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"", "its end moved later");
        Differs(Grants(Grant(Raised)), Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"", "its end dropped");
        Differs(Grants(Grant(Raised)) + ",\"valid_from\":\"2026-01-01T00:00:00Z\"", Grants(Grant(Cap)) + ",\"valid_from\":\"2026-06-01T00:00:00Z\"", "its start moved earlier");
        Assert.True(Signs(Card(Grants(Grant(Raised)) + ",\"valid_until\":\"2027-01-01T01:00:00+01:00\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"")),
            "the same end, written with another offset");
        Assert.True(Signs(Card(Grants(Grant(Raised)) + ",\"valid_until\":\"2027-01-01T00:00:00.000Z\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"")),
            "the same end, written to the millisecond");
        Differs(Grants(Grant(Raised)) + ",\"valid_until\":\"2027-01-01T00:00:01Z\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"", "its end one second later");
        Differs(Grants(Grant(Raised)) + ",\"valid_until\":\"next year\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"", "an end that can't be read");
        Assert.Equal("", Terms.Instant(null));
        Assert.Equal(Terms.Instant("2027-01-01T00:00:00Z"), Terms.Instant("2026-12-31T19:00:00-05:00"));
        Assert.NotEqual(Terms.Instant("soon"), Terms.Instant("later"));
        // A text that only looks like this code's own form of an instant is not a time, and never equals one.
        var end = Terms.Instant("2027-01-01T00:00:00Z");
        Assert.StartsWith("@", end);
        Assert.NotEqual(end, Terms.Instant(end));
        Differs(Grants(Grant(Raised)) + ",\"valid_until\":\"" + end + "\"", Grants(Grant(Cap)) + ",\"valid_until\":\"2027-01-01T00:00:00Z\"", "a draft whose end is that form as text");
        Assert.NotEqual(Terms.Instant("?soon"), Terms.Instant("soon"));
        // The iPad's formatter reads a time to its last character: one with a line break after it can't be read.
        Assert.Equal("?2027-01-01T00:00:00Z\n", Terms.Instant("2027-01-01T00:00:00Z\n"));
        Assert.Equal(Terms.Instant("2027-01-01T00:00:00Z"), Terms.Instant("2027-01-01T00:00:00.0004Z"));
        Assert.Equal("@1798761600000", Terms.Instant("2027-01-01T00:00:00Z"));
        // Who holds it.
        Differs(Grants(Grant(Raised)) + ",\"subject\":\"refunds-1\",\"subjects\":[\"refunds-1\",\"payouts-1\"]",
            Grants(Grant(Cap)) + ",\"subject\":\"refunds-1\",\"subjects\":[\"refunds-1\"]", "a holder added");
        Differs(Grants(Grant(Raised)) + ",\"subject\":\"payouts-1\"", Grants(Grant(Cap)) + ",\"subject\":\"refunds-1\",\"subjects\":[\"refunds-1\"]", "another first holder");
        Assert.True(Signs(Card(Grants(Grant(Raised)) + ",\"subject\":\"refunds-1\"",
                Grants(Grant(Cap)) + ",\"subject\":\"refunds-1\",\"subjects\":[\"refunds-1\",\"support-1\"]")),
            "a draft that names only the same first holder keeps the Card's holders");
        // Where it applies.
        Differs(Grants(Grant(Raised)) + ",\"domain\":\"other\"", Grants(Grant(Cap)) + ",\"domain\":\"shop\"", "another domain");
        Differs(Grants(Grant(Raised)) + ",\"environment\":\"live\"", Grants(Grant(Cap)) + ",\"environment\":\"test\"", "another environment");
        Differs(Grants(Grant(Raised)) + ",\"location\":\"Europe/Paris\"", Grants(Grant(Cap)) + ",\"location\":\"America/New_York\"", "another time zone for its daily totals");
        // What only a draft carries.
        Differs(Grants(Grant(Raised)) + ",\"hold_expiry\":\"24h\"", Grants(Grant(Cap)), "how long a hold waits, set by the draft");
        Differs(Grants(Grant(Raised)) + ",\"revoked_at\":\"2026-11-12T00:00:00Z\"", Grants(Grant(Cap)), "a draft that revokes");
    }

    /// <summary>The iPad's testACardMarkedUnchangedIsComparedOnEveryTerm: a Card marked unchanged is compared, not trusted.</summary>
    [Fact]
    public void A_Card_marked_unchanged_is_compared_on_every_term()
    {
        const string lying = """,{"revises":"c2","unchanged":true,"current":{"grants":[{"action":"list_charges","resources":{"parameter":"account","values":["acct_1"]}}],"valid_until":"2027-01-01T00:00:00Z"},"grants":[{"action":"list_charges"}],"valid_until":"2027-01-01T00:00:00Z"}""";
        Assert.False(Signs(Draft(extra: lying)), "marked unchanged, with its scope dropped");
        const string later = """,{"revises":"c2","unchanged":true,"current":{"grants":[{"action":"list_charges"}],"valid_until":"2027-01-01T00:00:00Z"},"grants":[{"action":"list_charges"}],"valid_until":"2030-01-01T00:00:00Z"}""";
        Assert.False(Signs(Draft(extra: later)), "marked unchanged, with its end moved later");
    }

    /// <summary>The iPad's testACardMarkedUnchangedMustReadAsInForce.</summary>
    [Fact]
    public void A_Card_marked_unchanged_must_read_as_in_force()
    {
        const string lying = """,{"revises":"c2","unchanged":true,"current":{"grants":[{"action":"list_charges"}]},"grants":[{"action":"list_charges"},{"action":"delete_customer"}]}""";
        Assert.False(Signs(Draft(extra: lying)), "marked unchanged, with an action added");
    }

    /// <summary>The iPad's testTheDraftMustBeThisHeldCallsAgentsAndFromThisHoldWhenKiffSays.</summary>
    [Fact]
    public void The_draft_must_be_this_held_call_s_agent_s_and_from_this_hold_when_KIFF_says()
    {
        var (after, now) = (Grants(Refund(Raised)), Grants(Refund(Cap)));
        Assert.False(Signs(Card(after, now, agent: "\"agent_id\":\"payouts-1\",")), "another agent's draft");
        Assert.False(Signs(Card(after, now), agent: null), "a held call with no agent");
        Assert.False(Signs(Card(after, now), agent: ""), "a held call with an empty agent");
        Assert.False(Signs(Card(after, now, from: ",\"from_hold\":\"ce_8\"")), "made from another held call");
        Assert.True(Signs(Card(after, now, from: ",\"from_hold\":\"ce_9\"")), "made from this one");
    }

    // The offer, read on the tap

    /// <summary>tafs cloud and KIFF, answering by method and path, with what was sent.</summary>
    sealed class Services
    {
        public readonly Dictionary<string, string> Responses = [];
        public readonly List<(string Request, string? Body)> Sent = [];

        public Task<CloudRead> Post(string path, byte[] body)
        {
            Sent.Add(("POST " + path, Encoding.UTF8.GetString(body)));
            return Task.FromResult(Answer("POST " + path));
        }

        public Task<CloudRead> Get(string path)
        {
            Sent.Add(("GET " + path, null));
            return Task.FromResult(Answer("GET " + path));
        }

        CloudRead Answer(string request) =>
            Responses.TryGetValue(request, out var json) ? CloudRead.Of(Json(json)) : new CloudRead(null, "refused", 409);

        public Task<(AllowRead? Read, string? Why)> ReadWhy(bool round) =>
            HoldAllow.ReadAsync(En, "taf_1", Held(["per_action_exceeded"]), round, Post, Get);

        public async Task<AllowRead?> Read(bool round) => (await ReadWhy(round)).Read;
    }

    /// <summary>The iPad's testTheOfferIsAskedOnTheTapAndTheDraftIsReadFromKiff.</summary>
    [Fact]
    public async Task The_offer_is_asked_on_the_tap_and_the_draft_is_read_from_KIFF()
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson();
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = Draft();
        var read = await services.Read(round: false);
        Assert.NotNull(read);
        Assert.True(read.Matches);
        Assert.Equal("v7", read.Draft?.Version);
        Assert.Equal("€350", read.Answer.Offer?.Round?.Words);
        Assert.Equal(("POST /v1/tafs/taf_1/holds/ce_9/allow", "{}"), services.Sent[0]);

        await services.Read(round: true);
        Assert.Equal(("POST /v1/tafs/taf_1/holds/ce_9/allow", """{"round":true}"""), services.Sent[^2]);
    }

    /// <summary>The iPad's testADraftThatIsntWhatWasOfferedDoesNotMatch.</summary>
    [Fact]
    public async Task A_draft_that_isnt_what_was_offered_does_not_match()
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson();
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = Draft(max: 99000);
        var read = await services.Read(round: false);
        Assert.NotNull(read);
        Assert.False(read.Matches);
        Assert.NotNull(read.Draft);
    }

    /// <summary>The iPad's testNoOfferReadsNoDraft: no offer, for a reason this app knows or not; tafs cloud's words, and no draft read.</summary>
    [Theory]
    [InlineData("no_change")]
    [InlineData("refused")]
    [InlineData("never")]
    [InlineData("something_new")]
    public async Task No_offer_reads_no_draft(string none)
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = "{\"none\":\"" + none + "\",\"words\":\"No Card limit holds this one back.\"}";
        var read = await services.Read(round: false);
        Assert.NotNull(read);
        Assert.Null(read.Answer.Offer);
        Assert.Equal(none, read.Answer.None);
        Assert.Equal("No Card limit holds this one back.", read.Answer.Words);
        Assert.Null(read.Draft);
        Assert.False(read.Matches);
        Assert.DoesNotContain(services.Sent, s => s.Request.StartsWith("GET "));
    }

    /// <summary>The card's button, as the iPad's offersAllow: live, a Cards manager, a listed taf, and a hold a change can lift.</summary>
    [Fact]
    public void The_button_needs_it_live_a_manager_a_taf_and_a_liftable_hold()
    {
        var call = Held(["per_action_exceeded"]);
        string[] live = ["holds.answer", "cards.manage", "holds.allow"];
        Assert.True(HoldAllow.Offers(call, live, managesCards: true, taf: "taf_1"));
        Assert.False(HoldAllow.Offers(call, ["holds.answer", "cards.manage"], true, "taf_1"), "not live");
        Assert.False(HoldAllow.Offers(call, live, false, "taf_1"), "not a manager");
        Assert.False(HoldAllow.Offers(call, live, true, null), "no taf in tafs cloud");
        Assert.False(HoldAllow.Offers(Held(["action_not_on_card"]), live, true, "taf_1"), "no change lifts it");
    }

    /// <summary>The confirm's body: the version read, and the held call only with the switch on, at the version seen.</summary>
    [Fact]
    public async Task The_confirm_signs_the_version_read_and_the_call_only_with_the_switch_on()
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson();
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = Draft();
        var sheet = AllowSheet.Open(Held(["per_action_exceeded"])).Loaded((await services.Read(round: false))!, round: false);
        Assert.Equal("/v1/me/agent-drafts/dr_9/confirm", HoldAllow.ConfirmPath("dr_9"));
        Assert.True(sheet.SendHeld);
        Assert.Equal("""{"version":"v7","hold":{"id":"ce_9","accounting_version":3}}""", Encoding.UTF8.GetString(sheet.Confirmation(En).Body!));
        Assert.Equal("""{"version":"v7"}""", Encoding.UTF8.GetString((sheet with { SendHeld = false }).Confirmation(En).Body!));

        // No version for the call: nothing signed, and why.
        var unseen = sheet with { Call = sheet.Call with { AccountingVersion = null } };
        Assert.Equal((null, En.Get("needs_you.answer_not_taken")), unseen.Confirmation(En));
        // Not what was offered, busy, or already confirmed: nothing to sign.
        Assert.Null((sheet with { Matches = false }).Confirmation(En).Body);
        Assert.Null((sheet with { Busy = true }).Confirmation(En).Body);
        Assert.Null((sheet with { Draft = sheet.Draft! with { Status = "confirmed" } }).Confirmation(En).Body);
    }

    /// <summary>
    /// Signing that fails sends nothing and never leaves the sheet waiting: Windows Hello closed says nothing; a TPM or
    /// Windows Hello error, an answer that can't be read, or a refusal says why. Signed, the confirm goes with its header.
    /// </summary>
    [Fact]
    public async Task A_failure_to_sign_sends_nothing_and_says_why()
    {
        var posted = new List<(string Path, string Body, string Header)>();
        Task<(int, string)> Post(string path, byte[] body, (string Header, string Value) s)
        {
            posted.Add((path, Encoding.UTF8.GetString(body), $"{s.Header}: {s.Value}"));
            return Task.FromResult((200, "{}"));
        }
        var body = HoldAllow.ConfirmBody("v7", null);
        async Task<(bool Done, string? Why, bool Stopped)> Confirm(Exception? thrown) => await HoldAllow.ConfirmAsync(En, "dr_9", body,
            (_, _) => thrown is null ? Task.FromResult(("KIFF-Passkey-Assertion", "t=x,a=y")) : Task.FromException<(string, string)>(thrown),
            Post, () => true, e => $"0x{e.HResult:X8}");

        Assert.Equal((false, null, false), await Confirm(new OperationCanceledException()));
        var tpm = new System.Security.Cryptography.CryptographicException("tpm");
        Assert.Equal((false, $"0x{tpm.HResult:X8}", false), await Confirm(tpm));
        Assert.Equal((false, "0x80004005", false), await Confirm(new System.Runtime.InteropServices.COMException("hello", unchecked((int)0x80004005))));
        Assert.False((await Confirm(new InvalidDataException("short"))).Done);
        Assert.Equal((false, "no user verification", false), await Confirm(new Tafs.Core.Approvals.PasskeyRefusedException("no user verification")));
        Assert.Empty(posted);

        Assert.Equal((true, null, false), await Confirm(null));
        Assert.Equal(("/v1/me/agent-drafts/dr_9/confirm", """{"version":"v7"}""", "KIFF-Passkey-Assertion: t=x,a=y"), Assert.Single(posted));

        // Another session began after the signature: nothing is sent.
        posted.Clear();
        var stopped = await HoldAllow.ConfirmAsync(En, "dr_9", body, (_, _) => Task.FromResult(("h", "v")), Post, () => false, _ => "");
        Assert.Equal((false, null, true), stopped);
        Assert.Empty(posted);
        // KIFF's refusal, read as KIFF's.
        var refused = await HoldAllow.ConfirmAsync(En, "dr_9", body, (_, _) => Task.FromResult(("h", "v")),
            (_, _, _) => Task.FromResult((409, """{"error":"version_changed"}""")), () => true, _ => "");
        Assert.Equal((false, "version_changed", false), refused);
    }

    /// <summary>Signed out after tafs cloud's offer, KIFF's draft isn't read, as the old account.</summary>
    [Fact]
    public async Task Signed_out_after_the_offer_no_draft_is_read()
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson();
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = Draft();
        var read = await HoldAllow.ReadAsync(En, "taf_1", Held(["per_action_exceeded"]), false, services.Post, services.Get, () => false);
        Assert.Equal((null, null), read);
        Assert.DoesNotContain(services.Sent, s => s.Request.StartsWith("GET "));
    }

    /// <summary>Signed out before the confirm, Windows Hello isn't asked and nothing is sent.</summary>
    [Fact]
    public async Task Signed_out_before_the_confirm_nothing_is_asked()
    {
        var asked = 0;
        var confirmed = await HoldAllow.ConfirmAsync(En, "dr_9", HoldAllow.ConfirmBody("v7", null),
            (_, _) => { asked++; return Task.FromResult(("h", "v")); }, (_, _, _) => Task.FromResult((200, "{}")), () => false, _ => "");
        Assert.Equal((false, null, true), confirmed);
        Assert.Equal(0, asked);
    }

    /// <summary>Stands in for webauthn.dll: signs authData ‖ sha256(clientDataJSON), with user verification.</summary>
    sealed class Authenticator(Tafs.Core.Tests.Kiff.TestSigner key) : IPasskeyAuthenticator
    {
        public byte[]? ClientData;
        public int Asked;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rp, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            Asked++;
            ClientData = clientDataJson;
            var authData = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rp)).Concat(new byte[] { 0x05, 0, 0, 0, 1 }).ToArray();
            return new PasskeyAssertion(authData, await key.SignDerAsync(authData.Concat(System.Security.Cryptography.SHA256.HashData(clientDataJson)).ToArray(), cancel));
        }
    }

    /// <summary>
    /// The confirm's one Windows Hello asserts exactly this request: its challenge is base64url(sha256) of kiff.request.v1 over
    /// the tenant, POST, the confirm's path, its body (switch on or off) and signed_at, which the header's t= carries. A session
    /// that doesn't approve with its passkey throws before anything is asked.
    /// </summary>
    [Fact]
    public async Task The_confirm_s_assertion_is_over_this_exact_request()
    {
        using var key = new Tafs.Core.Tests.Kiff.TestSigner();
        var auth = new Authenticator(key);
        var passkey = new PasskeyApprover(auth, Encoding.UTF8.GetBytes("this-pc"), true);
        var session = new Tafs.Core.Kiff.KeptSession("api.kiff.dev", "token", "t_01", "s1", "dev_pc01", Tafs.Core.Kiff.ApprovalMode.Passkey, DateTimeOffset.MaxValue, "k") { PasskeyId = "AQI" };
        const string at = "2026-11-15T23:00:00.250Z";
        var path = HoldAllow.ConfirmPath("dr_9");
        foreach (var body in new[] { HoldAllow.ConfirmBody("v7", ("ce_9", 3)), HoldAllow.ConfirmBody("v7", null) })
        {
            var (header, value) = await HoldAllow.SignConfirmAsync(session, passkey, path, body, at);
            Assert.Equal("KIFF-Passkey-Assertion", header);
            Assert.StartsWith($"t={at},a=", value);
            using var client = JsonDocument.Parse(auth.ClientData!);
            var message = Tafs.Core.Kiff.KiffSigning.RequestV1("t_01", "POST", "/v1/me/agent-drafts/dr_9/confirm", body, at);
            Assert.Equal(PasskeyApprover.Challenge(message), client.RootElement.GetProperty("challenge").GetString());
        }
        Assert.Equal(2, auth.Asked);

        foreach (var other in new[] { session with { Mode = Tafs.Core.Kiff.ApprovalMode.Key }, session with { Mode = Tafs.Core.Kiff.ApprovalMode.View }, session with { PasskeyId = null } })
            await Assert.ThrowsAsync<PasskeyRefusedException>(() => HoldAllow.SignConfirmAsync(other, passkey, path, HoldAllow.ConfirmBody("v7", null), at));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => HoldAllow.SignConfirmAsync(session, null, path, HoldAllow.ConfirmBody("v7", null), at));
        Assert.Equal(2, auth.Asked);
    }

    /// <summary>Rounding up keeps the person's switch and is offered once; the first read takes the offer's.</summary>
    [Fact]
    public async Task Rounding_up_keeps_the_switch_and_is_offered_once()
    {
        var services = new Services();
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson().Replace("\"send_held\":true", "\"send_held\":false");
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = Draft();
        var read = (await services.Read(round: false))!;
        var sheet = AllowSheet.Open(Held(["per_action_exceeded"])).Loaded(read, round: false);
        Assert.False(sheet.SendHeld);
        Assert.Equal("€350", sheet.RoundTo);
        var rounded = (sheet with { SendHeld = true }).Loaded(read, round: true);
        Assert.True(rounded.SendHeld);
        Assert.Null(rounded.RoundTo);
        Assert.False(sheet.NotOffered);
        Assert.True((sheet with { Matches = false }).NotOffered);
        Assert.Equal(En.Get("version.card_not_read", ("why", "refused")), sheet.Failed(En, "refused").Error);
    }

    /// <summary>
    /// An answer that can't be read, from tafs cloud or KIFF, reads as none with why, where the iPad throws: the service's
    /// refusal as it says it, or that it didn't answer as expected.
    /// </summary>
    [Fact]
    public async Task An_answer_that_cant_be_read_is_none_with_why()
    {
        var services = new Services();
        Assert.Equal((null, "refused"), await services.ReadWhy(round: false));
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = """{"offer":{"draft":"dr_9"}}""";
        Assert.Equal((null, En.Get("kiff.kiff_did_not_answer_expected")), await services.ReadWhy(round: false));
        services.Responses["POST /v1/tafs/taf_1/holds/ce_9/allow"] = OfferJson();
        Assert.Equal((null, "refused"), await services.ReadWhy(round: false));
        services.Responses["GET /v1/me/agent-drafts/dr_9"] = """{"id":"dr_9"}""";
        Assert.Equal((null, En.Get("kiff.kiff_did_not_answer_expected")), await services.ReadWhy(round: false));
    }

    /// <summary>The iPad's testAnOfferWithoutItsReplayStillReads: the replay couldn't be read.</summary>
    [Fact]
    public void An_offer_without_its_replay_still_reads()
    {
        var offer = Offer("""{"offer":{"changes":[{"mandate_id":"c1","kind":"per_action","before":15000,"after":32000,"unit":"EUR"}],"draft":"dr_9"}}""");
        Assert.Null(offer.Would);
        Assert.Null(offer.Round);
        Assert.Null(offer.SendHeld);
    }

    /// <summary>Every field the iPad's <c>AllowOffer</c> reads.</summary>
    [Fact]
    public void The_offer_is_read_whole()
    {
        var offer = Offer();
        Assert.Equal(new AllowChange("c1", "per_action", 15000, 32000, "EUR", null, null, null, "Refunds up to €150 each", "Refunds up to €320 each", false),
            Assert.Single(offer.Changes));
        Assert.Equal(new AllowWould(4, "With this Card, 4 more refunds would have gone out on their own (€1,180)"), offer.Would);
        Assert.Equal(("dr_9", "critical", true), (offer.Draft, offer.ConfirmKey, offer.SendHeld));
        Assert.Equal(new AllowRound(35000, "€350"), offer.Round);
        var resource = Offer("""{"offer":{"changes":[{"mandate_id":"c1","kind":"resource","parameter":"account","value":"acct_2","window":null,"later":1}],"draft":"dr_9","would":null}}""");
        Assert.Equal(new AllowChange("c1", "resource", Parameter: "account", Value: "acct_2"), Assert.Single(resource.Changes));
        Assert.Null(resource.Would);
    }

    /// <summary>As the iPad's synthesized decoder: a required field missing, or any field of another shape, fails the answer.</summary>
    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"none":3}""")]
    [InlineData("""{"words":true}""")]
    [InlineData("""{"offer":"dr_9"}""")]
    [InlineData("""{"offer":{"changes":[]}}""")]
    [InlineData("""{"offer":{"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":null,"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":9}}""")]
    [InlineData("""{"offer":{"changes":[{"kind":"per_action"}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[{"mandate_id":"c1"}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[{"mandate_id":"c1","kind":"per_action","before":"15000"}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[{"mandate_id":"c1","kind":"per_action","after":320.5}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[{"mandate_id":"c1","kind":"per_action","unit":2}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[{"mandate_id":"c1","kind":"per_action","rounded":"no"}],"draft":"dr_9"}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":"dr_9","would":{"calls":"4"}}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":"dr_9","would":4}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":"dr_9","round":{"after":"35000"}}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":"dr_9","send_held":"yes"}}""")]
    [InlineData("""{"offer":{"changes":[],"draft":"dr_9","confirm_key":1}}""")]
    public void The_offer_is_read_strictly(string json) => Assert.Null(AllowOffers.Read(Json(json)));

    /// <summary>The route, escaped as the iPad's <c>allowOffer</c>, and its body: <c>{}</c> for the exact figure, <c>{"round":true}</c> rounded.</summary>
    [Fact]
    public void The_route_and_its_body()
    {
        Assert.Equal("/v1/tafs/taf_1/holds/ce_9/allow", AllowOffers.Path("taf_1", "ce_9"));
        Assert.Equal("/v1/tafs/taf%2F1/holds/ce%209/allow", AllowOffers.Path("taf/1", "ce 9"));
        Assert.Equal("{}", Encoding.UTF8.GetString(AllowOffers.Body(round: false)));
        Assert.Equal("""{"round":true}""", Encoding.UTF8.GetString(AllowOffers.Body(round: true)));
    }

    /// <summary>A draft's Card keeps every term the comparison reads, in force and in the draft.</summary>
    [Fact]
    public void A_draft_s_Card_keeps_every_term()
    {
        var d = KiffDrafts.Read(Json("""
            {"id":"dr_9","agent_id":"refunds-1","status":"draft","version":"v7","cards":[{"revises":"c1",
              "grants":[{"action":"create_refund","resources":{"parameter":"account","values":["acct_1"]},"entity_states":["paid"],"next_steps":["return_first"]}],
              "aggregates":[{"quantity":"count","limit":20,"window":"day","measure":"attempts","per":"invoice_id"}],
              "subject":"refunds-1","subjects":["refunds-1","support-1"],"domain":"Shop","environment":"live","location":"Europe/Paris",
              "valid_from":"2026-01-01T00:00:00Z","valid_until":"2027-01-01T00:00:00Z","hold_expiry":"24h","revoked_at":null,
              "current":{"grants":[{"action":"create_refund","resources":{"parameter":"account"}}],"subject":"refunds-1"}}]}
            """));
        Assert.NotNull(d);
        var card = Assert.Single(d.Cards!);
        var grant = Assert.Single(card.Grants!);
        Assert.Equal("account", grant.Resources?.Parameter);
        Assert.Equal(["acct_1"], grant.Resources!.Values!);
        Assert.Equal(["paid"], grant.EntityStates!);
        Assert.Equal(["return_first"], grant.NextSteps!);
        Assert.Equal(new DraftTotal("count", 20, "day", null, null, "attempts", "invoice_id"), Assert.Single(card.Totals!));
        Assert.Equal(("refunds-1", "Shop", "live", "Europe/Paris"), (card.Subject, card.Domain, card.Environment, card.Location));
        Assert.Equal(["refunds-1", "support-1"], card.Subjects!);
        Assert.Equal(("2026-01-01T00:00:00Z", "2027-01-01T00:00:00Z", "24h"), (card.ValidFrom, card.ValidUntil, card.HoldExpiry));
        Assert.Null(card.RevokedAt);
        Assert.Null(card.Current!.Grants![0].Resources!.Values);
        Assert.Equal("refunds-1", card.Current.Subject);

        // The terms compare by value, lists and all, and in an order of their own.
        Assert.Equal(new Terms(card.Current), new Terms(card.Current));
        Assert.Equal(new Terms(card.Current).GetHashCode(), new Terms(card.Current).GetHashCode());
        Assert.Equal("shop", new Terms(card, card.Current).Domain);
    }
}
