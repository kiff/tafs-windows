using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// KIFF's held calls read as strictly as the iPad's synthesized <c>CardException</c> decoder, each under its holder as the
/// iPad's <c>holderName ?? aTaf</c>, and the answers' place as its <c>answers(refuseOnly:)</c>.
/// </summary>
[Part("approvals")]
public class KiffHeldStrictTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Base = """ "id":"a","subject":"r","action":"refund","entity_id":"e","status":"held","created_at":"2026-10-19T19:00:00Z" """;

    static KiffHold? Hold(string rest) => KiffHeld.Hold(JsonDocument.Parse("{" + Base + rest + "}").RootElement);

    /// <summary>A field of the wrong kind fails the read: never dropped, so what the card shows is what KIFF holds.</summary>
    [Theory]
    [InlineData(""","breaches":[],"parameters":{"amount":150.5,"fee":200}""")]
    [InlineData(""","breaches":[],"parameter_units":{"amount":{"unit":"EUR"}}""")]
    [InlineData(""","breaches":[],"next_steps":["return first",3]""")]
    [InlineData(""","breaches":[{"outcome":"per_action_exceeded","limit":"100"}]""")]
    [InlineData(""","breaches":[{"outcome":"x","unit_ambiguous":"no"}]""")]
    [InlineData(""","breaches":[{"limit":100}]""")]
    [InlineData(""","breaches":[],"kind":1""")]
    [InlineData(""","breaches":[],"can_approve_once":"yes" """)]
    [InlineData(""","breaches":[],"accounting_version":"3" """)]
    [InlineData(""","breaches":[],"count":1.5""")]
    [InlineData(""","breaches":[],"units_unavailable":1""")]
    [InlineData(""","breaches":[],"expires_at":"2026-10-19T19:10:00" """)]
    [InlineData(""","breaches":[],"expires_at":"10/19/2026" """)]
    [InlineData(""","breaches":[],"last_at":"yesterday" """)]
    [InlineData(""","breaches":[],"arguments":[1]""")]
    [InlineData(""","breaches":{}""")]
    public void A_field_of_the_wrong_kind_fails_the_read(string rest) => Assert.Null(Hold(rest));

    /// <summary>Null is absent; 200.0 is 200; the taf's words of another kind are left out, never the hold; fractions and offsets are kept.</summary>
    [Fact]
    public void Nulls_whole_numbers_and_the_taf_s_words()
    {
        var hold = Hold(""","breaches":[],"parameters":{"amount":200.0},"kind":null,"expires_at":"2026-10-19T19:10:00.123+02:00","taf_about":3""")!;
        Assert.Equal(200, hold.Parameters!["amount"]);
        Assert.Null(hold.Kind);
        Assert.Null(hold.TafAbout);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 17, 10, 0, 123, TimeSpan.Zero), hold.ExpiresAt!.Value.ToUniversalTime());
    }

    /// <summary>Setup's agent is Coach, with its avatar, and isn't in the sidebar's list of tafs' calls.</summary>
    [Fact]
    public void Coach_s_hold_is_under_Coach()
    {
        var hold = KiffHeld.Hold(JsonDocument.Parse("""{"id":"s","subject":"setup","action":"x","entity_id":"e","status":"held","breaches":[],"created_at":"2026-10-19T19:00:00Z"}""").RootElement)!;
        var call = KiffHeld.Calls(En, [hold], _ => null)[0];
        Assert.Equal(("Coach", false, true), (call.Taf, call.Listed, call.Avatar));
    }

    /// <summary>
    /// Where the answers go: who answers without holds.answer; the gate where Approve once would be; over a Card total, its
    /// line, the gate only for a manager's Extra room. The gate exactly while the PC can't approve (HeldAnswers.Approves): a
    /// Key session, a Passkey one with no passkey id, a view-only one or none keep it; a Passkey session with its id reads the
    /// line with no gate.
    /// </summary>
    [Fact]
    public void Where_the_answers_go()
    {
        const bool view = false;
        var once = KiffHeld.Calls(En, [Hold(""","breaches":[]""")!], _ => ("Rae", 1))[0];
        var total = KiffHeld.Calls(En, [Hold(""","breaches":[],"can_approve_once":false""")!], _ => ("Rae", 1))[0];
        Assert.True(once.ApprovableOnce);
        Assert.False(total.ApprovableOnce);
        Assert.Equal(new HeldAnswerLines(En.Get("needs_you.someone_answers"), false), NeedsYouScreen.Answers(En, once, mayAnswerHolds: false, managesCards: true, view));
        Assert.Equal(new HeldAnswerLines(En.Get("needs_you.someone_answers"), false), NeedsYouScreen.Answers(En, total, mayAnswerHolds: false, managesCards: true, view));
        Assert.Equal(new HeldAnswerLines(null, true), NeedsYouScreen.Answers(En, once, true, false, view));
        Assert.Equal(new HeldAnswerLines(En.Get("needs_you.over_total_manager"), true), NeedsYouScreen.Answers(En, total, true, true, view));
        Assert.Equal(new HeldAnswerLines(En.Get("needs_you.over_total"), false), NeedsYouScreen.Answers(En, total, true, false, view));
        foreach (var (mode, id) in new (Tafs.Core.Kiff.ApprovalMode, string?)[] { (Tafs.Core.Kiff.ApprovalMode.Key, "AQI"),
            (Tafs.Core.Kiff.ApprovalMode.Passkey, null), (Tafs.Core.Kiff.ApprovalMode.View, null) })
            Assert.Equal(new HeldAnswerLines(null, true), NeedsYouScreen.Answers(En, once, true, false, HeldAnswers.Approves(mode, id)));
        {
            var approves = HeldAnswers.Approves(Tafs.Core.Kiff.ApprovalMode.Passkey, "AQI");
            Assert.Equal(new HeldAnswerLines(null, false), NeedsYouScreen.Answers(En, once, true, false, approves));
            Assert.Equal(new HeldAnswerLines(En.Get("needs_you.over_total_manager"), false), NeedsYouScreen.Answers(En, total, true, true, approves));
            Assert.Equal(new HeldAnswerLines(En.Get("needs_you.over_total"), false), NeedsYouScreen.Answers(En, total, true, false, approves));
            Assert.Equal(new HeldAnswerLines(En.Get("needs_you.someone_answers"), false), NeedsYouScreen.Answers(En, once, false, true, approves));
        }
    }
}
