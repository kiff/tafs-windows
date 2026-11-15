using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Approve on Coach's approval card, as the iPad's <c>approve</c>: what's asked of tafs cloud, its decision read as strictly as
/// the iPad's decoder, the message this device may sign rebuilt from the card, and the report signed as a setup call.
/// </summary>
[Part("approvals")]
public class CoachApprovalTests
{
    const string Digest = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    static ApprovalDecision? Decision(string json) => CoachApproval.Read(Json(json));

    static ApprovalAsk Ask(string ask) => Decision($$"""{"outcome": "sign", "ask": {{ask}}}""")!.Ask!;

    /// <summary>
    /// tafs cloud's decisions as the iPad's test has them: a connect step with planque's body as tafs cloud built it, a cards
    /// step with KIFF's draft at its version, and answers with their note, conversation and run.
    /// </summary>
    [Fact]
    public void A_decision_reads_as_tafs_cloud_sends_it()
    {
        var connect = Decision($$"""
            {"outcome": "sign", "ask": {"id": "ask_1", "step": "connect", "message": "tafs.connect.v1|t_01|g_01|{{Digest}}",
             "reason": "Connect Stripe to Refunds",
             "planque": {"grant": "g_01", "body": {"digest": "{{Digest}}", "kiff_profile": {"profile_sha256": "ab", "words_sha256": "cd"} } } } }
            """)!;
        Assert.Equal("sign", connect.Outcome);
        var ask = connect.Ask!;
        Assert.Equal(("ask_1", "connect", $"tafs.connect.v1|t_01|g_01|{Digest}", "Connect Stripe to Refunds"), (ask.Id, ask.Step, ask.Message, ask.Reason));
        Assert.Equal(("g_01", Digest), (ask.Planque!.Grant, ask.Planque.Digest));
        // planque's body as tafs cloud built it, sent as it is.
        Assert.Equal("ab", ask.Planque.Body.GetProperty("kiff_profile").GetProperty("profile_sha256").GetString());
        Assert.Equal($$"""{"digest": "{{Digest}}", "kiff_profile": {"profile_sha256": "ab", "words_sha256": "cd"} }""", ask.Planque.Body.GetRawText());
        Assert.Null(ask.Kiff);
        Assert.Null(ask.Key);

        var cards = Ask("""{"id": "ask_2", "step": "cards", "message": "tafs.cards.v1|t_01|d_01|v3", "reason": "Give refunds its Card", "kiff": {"draft": "d_01", "version": "v3"}}""");
        Assert.Equal(new ApprovalKiff("d_01", "v3"), cards.Kiff);
        Assert.Null(cards.Planque);

        Assert.Equal(new ApprovalDecision("answered", "approved", null, null, "s1", "r1"),
            Decision("""{"outcome": "answered", "answer": "approved", "session": "s1", "run": "r1"}"""));
        Assert.Equal(new ApprovalDecision("answered", "already approved: Stripe is connected and its Card is confirmed", "Already approved", null, "s1", "r1"),
            Decision("""{"outcome": "answered", "answer": "already approved: Stripe is connected and its Card is confirmed", "note": "Already approved", "session": "s1", "run": "r1"}"""));
        Assert.Equal(new ApprovalDecision("read_again", null, "This card is out of date.", null, null, null),
            Decision("""{"outcome": "read_again", "note": "This card is out of date.", "ask": null, "later": {"field": 1}}"""));
        Assert.Equal("sign_in_not_confirmed", CoachApproval.SignInNotConfirmed);
    }

    /// <summary>The key a call takes is a hint, read leniently as the iPad's <c>LenientItem</c>: another shape is none, never a failed decision.</summary>
    [Fact]
    public void The_key_hint_is_read_leniently()
    {
        const string CardsAsk = """{"id": "a", "step": "cards", "message": "m", "reason": "r", "kiff": {"draft": "d", "version": "v"}""";
        Assert.Equal("window", Ask(CardsAsk + """, "key": "window"}""").Key);
        Assert.Equal("critical", Ask(CardsAsk + """, "key": "critical"}""").Key);
        Assert.Null(Ask(CardsAsk + """, "key": 5}""").Key);
        Assert.Null(Ask(CardsAsk + """, "key": {"kind": "window"}}""").Key);
        Assert.Null(Ask(CardsAsk + """, "key": null}""").Key);
    }

    /// <summary>Read as strictly as the iPad's decoder: a required field missing, or any field of another shape, fails the decision.</summary>
    [Fact]
    public void A_decision_of_another_shape_is_none()
    {
        foreach (var json in new[]
        {
            """{"answer": "approved"}""",
            """{"outcome": 1}""",
            """{"outcome": "answered", "note": 1}""",
            """{"outcome": "answered", "answer": true}""",
            """{"outcome": "answered", "session": ["s1"]}""",
            """{"outcome": "answered", "run": 2}""",
            """{"outcome": "sign", "ask": "ask_1"}""",
            """{"outcome": "sign", "ask": {"step": "cards", "message": "m", "reason": "r"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "message": "m", "reason": "r"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "cards", "reason": "r"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "cards", "message": "m"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "cards", "message": 1, "reason": "r"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": {"body": {"digest": "d"}}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": {"grant": "g"}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": {"grant": "g", "body": "d"}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": {"grant": "g", "body": {}}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": {"grant": "g", "body": {"digest": "d", "kiff_profile": {"profile_sha256": "ab"}}}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "connect", "message": "m", "reason": "r", "planque": "g"}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "cards", "message": "m", "reason": "r", "kiff": {"draft": "d"}}}""",
            """{"outcome": "sign", "ask": {"id": "a", "step": "cards", "message": "m", "reason": "r", "kiff": {"draft": "d", "version": 3}}}""",
            """["outcome"]""",
        })
            Assert.Null(Decision(json));
    }

    /// <summary>
    /// As the iPad's <c>testTheExpectedMessageIsRebuiltFromTheCardAndTheAccount</c>: the message is rebuilt from the card and
    /// the account, never taken from the ask. The digest and version are the ones shown; nothing shown, nothing signed.
    /// </summary>
    [Fact]
    public void The_expected_message_is_rebuilt_from_the_card_and_the_account()
    {
        // The card: grant g_01, no draft; withDraft: grant g_01 and draft d_01.
        var connect = Ask("""{"id": "ask_1", "step": "connect", "message": "x", "reason": "r", "planque": {"grant": "g_01", "body": {"digest": "d1"}}}""");
        Assert.Equal("tafs.connect.v1|t_01|g_01|d1", CoachApproval.ExpectedMessage(connect, "g_01", null, "t_01", "d1", null));
        Assert.Null(CoachApproval.ExpectedMessage(connect, "g_01", null, "t_01", "d2", null));
        Assert.Null(CoachApproval.ExpectedMessage(connect, "g_01", null, "t_01", null, null));
        // Another grant than the card's, or a card with none.
        Assert.Null(CoachApproval.ExpectedMessage(connect, "g_02", null, "t_01", "d1", null));
        Assert.Null(CoachApproval.ExpectedMessage(connect, null, null, "t_01", "d1", null));

        var cards = Ask("""{"id": "ask_1", "step": "cards", "message": "x", "reason": "r", "kiff": {"draft": "d_01", "version": "v3"}}""");
        Assert.Equal("tafs.cards.v1|t_01|d_01|v3", CoachApproval.ExpectedMessage(cards, "g_01", "d_01", "t_01", null, "v3"));
        Assert.Null(CoachApproval.ExpectedMessage(cards, "g_01", "d_01", "t_01", null, "v2"));
        Assert.Null(CoachApproval.ExpectedMessage(cards, "g_01", "d_01", "t_01", null, null));
        Assert.Null(CoachApproval.ExpectedMessage(cards, "g_01", null, "t_01", null, "v3"));
        Assert.Null(CoachApproval.ExpectedMessage(cards, "g_01", "d_02", "t_01", null, "v3"));

        var other = Ask("""{"id": "ask_1", "step": "resume", "message": "x", "reason": "r"}""");
        Assert.Null(CoachApproval.ExpectedMessage(other, "g_01", null, "t_01", "d1", null));
        // A step with neither planque nor KIFF named.
        Assert.Null(CoachApproval.ExpectedMessage(Ask("""{"id": "a", "step": "connect", "message": "x", "reason": "r"}"""), "g_01", null, "t_01", "d1", null));
        Assert.Null(CoachApproval.ExpectedMessage(Ask("""{"id": "a", "step": "cards", "message": "x", "reason": "r"}"""), "g_01", "d_01", "t_01", null, "v3"));
    }

    /// <summary>
    /// As the iPad's <c>stepMatchesMark</c>: a card marked as signing the Card takes only a step that signs it, one marked as
    /// not only a step that doesn't; with no mark, any step.
    /// </summary>
    [Fact]
    public void The_step_asked_must_match_the_card_s_mark()
    {
        foreach (var step in new[] { "connect", "cards", "key_and_cards", "tools_and_cards", "resume" })
        {
            Assert.True(CoachApproval.StepMatchesMark(null, step));
            Assert.True(CoachApproval.StepMatchesMark(new CardSigns(null, "Its Card: you approve it next", null), step));
        }
        var signsCard = new CardSigns(true, null, null);
        Assert.True(CoachApproval.StepMatchesMark(signsCard, "cards"));
        Assert.True(CoachApproval.StepMatchesMark(signsCard, "key_and_cards"));
        Assert.True(CoachApproval.StepMatchesMark(signsCard, "tools_and_cards"));
        Assert.False(CoachApproval.StepMatchesMark(signsCard, "connect"));
        Assert.False(CoachApproval.StepMatchesMark(signsCard, "resume"));

        var cardNext = new CardSigns(false, "Its Card: you approve it next", "window");
        Assert.True(CoachApproval.StepMatchesMark(cardNext, "connect"));
        Assert.False(CoachApproval.StepMatchesMark(cardNext, "cards"));
        Assert.False(CoachApproval.StepMatchesMark(cardNext, "tools_and_cards"));
    }

    /// <summary>
    /// What's asked of tafs cloud, as the iPad's <c>ApprovalAskBody</c>: the grant and what the card showed, with the
    /// conversation; a field with no value left out. Only the cards step is signed here, so neither words nor can is sent.
    /// </summary>
    [Fact]
    public void Approve_asks_tafs_cloud_with_what_the_card_showed()
    {
        Assert.Equal("/v1/tafs/taf-setup/components/connect-stripe/approval", CoachApproval.Path("taf-setup", "connect-stripe"));
        Assert.Equal("/v1/tafs/taf%20setup/components/a%2Fb/approval", CoachApproval.Path("taf setup", "a/b"));

        Assert.Equal($$"""{"grant":"g_01","shown_digest":"{{Digest}}","session":"s1"}""",
            Encoding.UTF8.GetString(CoachApproval.AskBody("g_01", Digest, null, null, "s1")));
        Assert.Equal($$"""{"grant":"g_01","shown_digest":"{{Digest}}","draft":"d_01","shown_draft":"v3"}""",
            Encoding.UTF8.GetString(CoachApproval.AskBody("g_01", Digest, "d_01", "v3", null)));
        Assert.Equal("""{"grant":"g_01"}""", Encoding.UTF8.GetString(CoachApproval.AskBody("g_01", null, null, null, null)));
    }

    /// <summary>
    /// What came of the call, as the iPad's <c>approvalReport</c>: to the ask's own path, never an answer, signed as a setup call
    /// (<c>kiff.request.v1</c> over exactly the body sent) in <c>KIFF-Device-Signature</c>.
    /// </summary>
    [Fact]
    public async Task The_report_says_what_came_of_the_call_signed_as_a_setup_call()
    {
        var path = CoachApproval.ReportPath("taf-setup", "refunds-card", "ask_2");
        Assert.Equal("/v1/tafs/taf-setup/components/refunds-card/approval/ask_2", path);
        Assert.Equal("/v1/tafs/taf-setup/components/refunds-card/approval/a%2Fb", CoachApproval.ReportPath("taf-setup", "refunds-card", "a/b"));

        var body = CoachApproval.ReportBody("confirmed", null, null, "s1");
        Assert.Equal("""{"result":"confirmed","session":"s1"}""", Encoding.UTF8.GetString(body));
        Assert.Equal("""{"result":"failed","why":"KIFF didn't answer"}""", Encoding.UTF8.GetString(CoachApproval.ReportBody("failed", null, "KIFF didn't answer", null)));
        Assert.Equal("""{"result":"listed","status":"listed"}""", Encoding.UTF8.GetString(CoachApproval.ReportBody("listed", "listed", null, null)));
        using (var sent = JsonDocument.Parse(body))
            Assert.False(sent.RootElement.TryGetProperty("answer", out _), "this device never builds an answer");

        const string SignedAt = "2026-11-15T18:00:00.123Z";
        var message = CoachApproval.ReportMessage("t_01", path, body, SignedAt);
        Assert.StartsWith("kiff.request.v1\nt_01\nPOST\n/v1/tafs/taf-setup/components/refunds-card/approval/ask_2\n", Encoding.UTF8.GetString(message));
        Assert.Equal(KiffSigning.Message("kiff.request.v1", "t_01", "POST", path, KiffSigning.Sha256Hex(body), SignedAt), message);

        using var key = new TestSigner();
        var header = CoachApproval.ReportSignature(SignedAt, await key.SignDerAsync(message));
        Assert.StartsWith($"t={SignedAt},v1=", header);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, message, Convert.FromBase64String(header[$"t={SignedAt},v1=".Length..])));
    }
}
