using System.Text;
using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>A Card unit's requests, as the iPad's CardUnitExceptionPanel, discussCardUnit and dismissCardUnit.</summary>
[Part("approvals")]
public sealed class CardUnitsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    const string At = "2026-11-23T07:00:00.000Z";

    static readonly CardUnitException Ask = new("ce_7", "Refunds wants a bigger unit", "It asks for €200 a refund.", "Asked twice this week",
        "Talk it over with Coach", "Dismiss", "Let's talk about the refund unit", false, "taf_1", "Refunds");

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    static CloudRead Held(string kind = "card_unit", string status = "held", string subject = "agt_refunds", string version = ",\"accounting_version\":4") =>
        CloudRead.Of(Json($$"""{"id":"ce_7","subject":"{{subject}}","action":"card_unit","entity_id":"e","status":"{{status}}","breaches":[],"created_at":"2026-11-23T06:00:00Z","kind":"{{kind}}"{{version}}}"""));

    [Fact]
    public void Home_lists_its_requests_with_their_taf()
    {
        var listed = CardUnits.Read(Json("""
            {"card_unit_exceptions": [
              {"id": "ce_7", "kind": "card_unit", "taf": "taf_1", "name": "Refunds", "title": "Refunds wants a bigger unit", "words": "It asks for €200 a refund.",
               "history": "Asked twice this week", "action_title": "Talk it over with Coach", "coach_text": "Let's talk", "dismiss_title": "Dismiss", "can_approve_once": false},
              {"id": "ce_8", "title": "No words"}, "ce_9"]}
            """));
        var one = Assert.Single(listed);
        Assert.Equal(("ce_7", "taf_1", "Refunds", "Talk it over with Coach", "Dismiss"), (one.Id, one.Taf, one.Name, one.ActionTitle, one.DismissTitle));
        Assert.Empty(CardUnits.Read(Json("""{"card_unit_exceptions": null}""")));
        Assert.Empty(CardUnits.Read(Json("""{}""")));
        // Dismissed here: gone from the list at once.
        Assert.Empty(CardUnits.Shown(listed, new HashSet<string> { "ce_7" }));
    }

    /// <summary>Read again from KIFF first: only a Card unit's request, still held, for this taf's agent, with its version, is answered.</summary>
    [Theory]
    [InlineData("card_unit", "held", "agt_refunds", true)]
    [InlineData("card_hold", "held", "agt_refunds", false)]
    [InlineData("card_unit", "approved", "agt_refunds", false)]
    [InlineData("card_unit", "held", "agt_other", false)]
    public void Only_the_request_shown_is_dismissed(string kind, string status, string subject, bool still) =>
        Assert.Equal(still, CardUnits.StillHeld(KiffHeld.Hold(Held(kind, status, subject).Answer!.Value)!, "ce_7", "agt_refunds"));

    /// <summary>KIFF answering with another request than the one shown: nothing is signed for it.</summary>
    [Fact]
    public async Task Another_request_read_back_is_never_signed()
    {
        var f = new Fakes { Read = CloudRead.Of(JsonDocument.Parse("""{"id":"ce_8","subject":"agt_refunds","action":"card_unit","entity_id":"e","status":"held","breaches":[],"created_at":"2026-11-23T06:00:00Z","kind":"card_unit","accounting_version":4}""").RootElement) };
        Assert.Equal("This request changed. Read it again before dismissing it.", (await f.Dismiss()).Why);
        Assert.Single(f.Said);
    }

    sealed class Fakes
    {
        public CloudRead Read = Held();
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public (int, string) Answer = (200, "{}");
        public readonly List<string> Said = [];

        public Task<CardUnits.Dismissed> Dismiss() => CardUnits.DismissAsync(En, Ask, "agt_refunds", "t_1",
            path => { Said.Add("read " + path); return Task.FromResult(Read); },
            prompt => { Said.Add("hello " + prompt); return Task.FromResult(Confirmed); },
            message => { Said.Add("sign " + Encoding.UTF8.GetString(message).Replace('\n', '|')); return Task.FromResult("c2ln"); },
            (path, body) => { Said.Add("post " + path + " " + Encoding.UTF8.GetString(body)); return Task.FromResult(Answer); },
            () => At, () => true);
    }

    [Fact]
    public async Task Dismiss_reads_it_again_asks_Windows_Hello_then_refuses_it_signed()
    {
        var f = new Fakes();
        Assert.True((await f.Dismiss()).Done);
        Assert.Equal([
            "read /v1/me/card-exceptions/ce_7",
            "hello Dismiss Refunds wants a bigger unit",
            "sign kiff.answer.v1|t_1|ce_7|reject|||4|" + At,
            "post /v1/me/card-exceptions/ce_7/reject {\"signature\":{\"signed_at\":\"" + At + "\",\"value\":\"c2ln\"}}",
        ], f.Said);
    }

    [Fact]
    public async Task A_request_that_changed_or_wasn_t_confirmed_sends_nothing()
    {
        var changed = new Fakes { Read = Held(status: "approved") };
        Assert.Equal("This request changed. Read it again before dismissing it.", (await changed.Dismiss()).Why);
        Assert.Single(changed.Said);
        var noVersion = new Fakes { Read = Held(version: "") };
        Assert.Equal("This request changed. Read it again before dismissing it.", (await noVersion.Dismiss()).Why);
        var closed = new Fakes { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal("Not dismissed: it wasn't confirmed.", (await closed.Dismiss()).Why);
        Assert.DoesNotContain(closed.Said, s => s.StartsWith("sign", StringComparison.Ordinal) || s.StartsWith("post", StringComparison.Ordinal));
        var unread = new Fakes { Read = CloudRead.Unreached(En) };
        Assert.StartsWith("The request wasn't dismissed. ", (await unread.Dismiss()).Why);
    }

    [Fact]
    public async Task KIFF_refusing_says_so_and_it_stays()
    {
        var refused = new Fakes { Answer = (409, """{"error": "not_held"}""") };
        var said = await refused.Dismiss();
        Assert.False(said.Done);
        Assert.StartsWith("The request wasn't dismissed. ", said.Why);
    }
}
