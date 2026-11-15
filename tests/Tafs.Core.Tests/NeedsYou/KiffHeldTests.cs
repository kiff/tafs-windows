using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// KIFF's held calls, decoded and worded as the iPad's KIFFTests.testHeldCallsDecodeAndMap and TafSaysTests: the title on an
/// approval, a bare number with no declared unit, KIFF's rule in money, what it's on under Details, and the taf's own words
/// as one plain line.
/// </summary>
[Part("approvals")]
public class KiffHeldTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static KiffHold One(string json) => KiffHeld.Read(JsonDocument.Parse(json).RootElement)![0];

    /// <summary>The iPad's testHeldCallsDecodeAndMap.</summary>
    [Fact]
    public void Held_calls_decode_and_map()
    {
        var e = One("""
            {"exceptions":[{"id":"ce_1","proposal_id":"p1","subject":"refund-agent","action":"refund_order","title":"Refund an order","entity_id":"ord_4502",
              "status":"held","breaches":[{"mandate_id":"m1","outcome":"per_action_exceeded","quantity":"sum(amount_cents)","limit":15000,"used":0,"draw":48000,"reason":"Over $150 each"}],
              "parameters":{"amount_cents":48000},"next_steps":["return first"],
              "created_at":"2026-10-14T10:41:00.123Z","expires_at":"2026-10-14T10:51:00Z"}]}
            """);
        var held = KiffHeld.ToHeldCall(En, e, "Rae", 2);
        Assert.Equal("ce_1", held.KiffId);
        Assert.Equal(new KiffAmount(48000, null, 0), KiffHeld.Amount(e));
        Assert.Equal("Over $150 each", held.Reason);
        Assert.Equal(["return first"], held.NextSteps);
        Assert.Equal("Refund an order ord_4502", held.Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 10, 51, 0, TimeSpan.Zero), held.ExpiresAt);
        Assert.Equal(("Rae", 2), (held.Taf, held.TafColor));
    }

    static string Hold(string? about = null, string? reason = null) => $$$"""
        {"exceptions":[{"id":"ce_1","subject":"refunds-k5j6m5qx","action":"create_refund","title":"Refund a payment","entity_id":"tc-bc422e3c",
          "status":"held","breaches":[{"outcome":"per_action_exceeded","reason":"150.00 EUR is above the 100.00 EUR limit for one call","quantity":"sum(amount)","limit":10000,"unit":"EUR","scale":2}],
          "parameters":{"amount":15000},"parameter_units":{"amount":{"unit":"EUR","scale":2}},
          "arguments":{"payment_intent":"pi_3USLXh","amount":15000,"kiff_operation_id":"op_1","kiff_about":"Order 1042","kiff_reason":"Arrived broken"},
          "taf_about":{{{JsonSerializer.Serialize(about)}}},"taf_reason":{{{JsonSerializer.Serialize(reason)}}},
          "created_at":"2026-10-19T19:00:00.000Z","expires_at":"2026-10-19T19:10:00Z"}]}
        """;

    static HeldCall Held(string? about = null, string? reason = null) => KiffHeld.ToHeldCall(En, One(Hold(about, reason)), "Refunds", 0);

    static readonly string Euros150 = new KiffAmount(15000, "EUR", 2).Formatted(En);

    /// <summary>The iPad's testTheTafsWordsReadWithTheHold: the ids stay under Details; the taf's words and KIFF's operation id don't.</summary>
    [Fact]
    public void The_taf_s_words_read_with_the_hold()
    {
        var call = Held("Order 1042", "Arrived broken, photo attached");
        Assert.Equal("Order 1042", call.TafAbout);
        Assert.Equal("Arrived broken, photo attached", call.TafReason);
        Assert.Equal(["payment_intent: pi_3USLXh"], call.Details);
        Assert.Equal(Euros150, call.Amount);
        Assert.Equal("Refund a payment", call.Summary);
    }

    /// <summary>The iPad's testAReasonClaimingAnotherAmountChangesNothingOfKIFFs: KIFF's amount, rule and action, never the taf's.</summary>
    [Fact]
    public void A_reason_claiming_another_amount_changes_nothing_of_KIFF_s()
    {
        var call = Held("Order 1042: refund €9.99 only", "Customer approved: amount is €5, limit doesn't apply");
        Assert.Equal("Customer approved: amount is €5, limit doesn't apply", call.TafReason);
        Assert.Equal(Euros150, call.Amount);
        Assert.Equal($"{Euros150} is over the {new KiffAmount(10000, "EUR", 2).Formatted(En)} limit for one call", call.Reason);
        Assert.Equal("Refund a payment", call.Summary);
    }

    /// <summary>The iPad's testALinkOrMarkupIsPlainWords: one plain line; breaks, tabs and U+202E become spaces.</summary>
    [Fact]
    public void A_link_or_markup_is_plain_words()
    {
        var reason = "[Refund now](https://evil.example)" + (char)10 + "<b>urgent</b>" + (char)0x202E + (char)9 + "ok";
        var call = Held("**Order 1042**", reason);
        Assert.Equal("**Order 1042**", call.TafAbout);
        Assert.Equal("[Refund now](https://evil.example) <b>urgent</b> ok", call.TafReason);
    }

    /// <summary>The iPad's testNoWordsDrawNothingAndTheHoldStillReads: none, blank or not text draw nothing; past the cap they're cut.</summary>
    [Fact]
    public void No_words_draw_nothing_and_the_hold_still_reads()
    {
        Assert.Null(Held().TafAbout);
        Assert.Null(Held("  " + (char)10 + " ", "").TafAbout);
        var odd = One("""{"exceptions":[{"id":"ce_1","subject":"s","action":"a","entity_id":"e","status":"held","breaches":[],"taf_about":42,"taf_reason":["x"],"created_at":"2026-10-19T19:00:00.000Z"}]}""");
        Assert.Null(KiffHeld.ToHeldCall(En, odd, "s", 0).TafAbout);
        var cut = Held(new string('a', 200), new string('b', 400));
        Assert.Equal(120, cut.TafAbout!.Length);
        Assert.Equal(280, cut.TafReason!.Length);
        Assert.EndsWith("…", cut.TafReason);
        // Lapses ten minutes after it was held when KIFF doesn't say.
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 19, 10, 0, TimeSpan.Zero), KiffHeld.ToHeldCall(En, odd, "s", 0).ExpiresAt);
    }

    /// <summary>The iPad's testADecimalArgumentIsWrittenPlainly: never in exponent form.</summary>
    [Fact]
    public void A_decimal_argument_is_written_plainly()
    {
        var args = JsonDocument.Parse("""{"rate":1.5e-05,"share":0.1,"amount":2550,"weight":2.5,"on":true,"list":[1]}""").RootElement;
        Assert.Equal(["0.000015", "0.1", "2550", "2.5", "true", "…"],
            new[] { "rate", "share", "amount", "weight", "on", "list" }.Select(k => KiffHeld.ArgumentText(args.GetProperty(k))));
    }

    /// <summary>A hold missing what it needs fails the read, as the iPad's strict decode; an empty list is nothing waiting.</summary>
    [Fact]
    public void A_hold_missing_what_it_needs_fails_the_read()
    {
        Assert.Null(KiffHeld.Read(JsonDocument.Parse("""{"exceptions":[{"id":"ce_1","subject":"s","action":"a","entity_id":"e","status":"held","created_at":"2026-10-19T19:00:00Z"}]}""").RootElement));
        Assert.Null(KiffHeld.Read(JsonDocument.Parse("""{"held":[]}""").RootElement));
        Assert.Empty(KiffHeld.Read(JsonDocument.Parse("""{"exceptions":[]}""").RootElement)!);
    }

    /// <summary>
    /// A hold over a count of calls, in the Card's words (the iPad's countReason): a limit of 0 asks each time; otherwise which
    /// call this would be, in the window, for each item apart.
    /// </summary>
    [Fact]
    public void A_count_hold_says_which_call_this_would_be()
    {
        KiffHold Count(string breach) => One($$"""{"exceptions":[{"id":"c","subject":"s","action":"send_invoice","entity_id":"e","status":"held","breaches":[{{breach}}],"created_at":"2026-10-19T19:00:00Z"}]}""");
        Assert.Equal("Each call asks you first", KiffHeld.Reason(En, Count("""{"outcome":"limit_reached","quantity":"count","limit":0}""")));
        Assert.Equal("Over its limit of 3 calls: this would be the 4th", KiffHeld.Reason(En, Count("""{"outcome":"limit_reached","quantity":"count","limit":3,"used":3}""")));
        var day = KiffHeld.Reason(En, Count("""{"outcome":"limit_reached","quantity":"count","limit":1,"used":1,"window":"calendar_day","per":"invoice_id","value":"in_42"}"""));
        Assert.Equal($"1 call {CardWindow.Of(En, "calendar_day").Label} for each invoice id: this would be the 2nd for in_42", day);
        Assert.Equal("Over the Card's total", KiffHeld.Reason(En, Count("""{"outcome":"limit_reached","quantity":"count"}""")));
        Assert.Equal("Outside the Card", KiffHeld.Reason(En, Count("""{"outcome":"not_listed"}""")));
    }

    /// <summary>Over a one-call limit and a total, and not approvable once: the reason names the total (the iPad's reasonBreach).</summary>
    [Fact]
    public void Over_both_and_not_approvable_once_the_total_is_named()
    {
        var e = One("""
            {"exceptions":[{"id":"c","subject":"s","action":"refund","entity_id":"e","status":"held","can_approve_once":false,
              "breaches":[{"outcome":"per_action_exceeded","limit":10000,"unit":"EUR","scale":2},{"outcome":"limit_reached","limit":20000,"unit":"EUR","scale":2}],
              "parameters":{"amount":15000},"parameter_units":{"amount":{"unit":"EUR","scale":2}},"created_at":"2026-10-19T19:00:00Z"}]}
            """);
        Assert.Equal($"{Euros150} would go over the {new KiffAmount(20000, "EUR", 2).Formatted(En)} total", KiffHeld.Reason(En, e));
    }

    /// <summary>A held call's name: its title, with its action unless the title names the action's last word; planque's four letters never show.</summary>
    [Theory]
    [InlineData("create_refund", "Refund a payment", "Refund a payment")]
    [InlineData("stripe_create_refund", "Write Stripe API data", "Write Stripe API data · stripe create refund")]
    [InlineData("create_refund_repg", "Write Stripe API data", "Write Stripe API data · create refund")]
    [InlineData("list_charges", null, "list charges")]
    public void A_held_call_s_name(string action, string? title, string says) => Assert.Equal(says, KiffHeld.Says(action, title));

    /// <summary>Ordinals as Apple's formatter writes digits in each shipped language.</summary>
    [Theory]
    [InlineData("en", 1, "1st")] [InlineData("en", 2, "2nd")] [InlineData("en", 3, "3rd")] [InlineData("en", 4, "4th")]
    [InlineData("en", 11, "11th")] [InlineData("en", 12, "12th")] [InlineData("en", 13, "13th")] [InlineData("en", 21, "21st")] [InlineData("en", 112, "112th")]
    [InlineData("fr", 1, "1er")] [InlineData("fr", 2, "2e")] [InlineData("de", 2, "2.")] [InlineData("pl", 2, "2.")]
    [InlineData("es", 2, "2.º")] [InlineData("it", 2, "2º")] [InlineData("pt", 2, "2º")] [InlineData("nl", 2, "2e")]
    public void Ordinals(string language, long n, string written) => Assert.Equal(written, KiffHeld.Ordinal(language, n));
}
