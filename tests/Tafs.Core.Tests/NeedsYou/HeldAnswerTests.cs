using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// Answering a held call, as the iPad's answer(_:with:), kiffAnswer and KIFFClient.answerMessage, signed as the desktop spec
/// says (§2.5, §2.6): approve once and extra room with a passkey assertion over kiff.answer.v1, a refusal with the device key.
/// </summary>
[Part("approvals")]
public class HeldAnswerTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    const string At = "2026-11-15T21:00:00.000Z";

    static HeldCall Call(string? id = "ce_1", int? version = 3) =>
        new("Refunds", 0, "Refund a payment", "€150.00", "", "Over €100.00 each", DateTimeOffset.UtcNow.AddMinutes(9), ["return first"])
        { KiffId = id, AccountingVersion = version };

    static readonly string Nl = ((char)10).ToString();

    /// <summary>kiff.answer.v1: tenant, hold, answer, next step or empty, the note's sha256 or empty, version, signed-at.</summary>
    [Fact]
    public void The_answer_message()
    {
        Assert.Equal(string.Join(Nl, "kiff.answer.v1", "t_1", "ce_1", "approve-once", "", "", "3", At),
            Encoding.UTF8.GetString(HeldAnswers.Message("t_1", "ce_1", new HeldAnswer.ApproveOnce(), 3, At)));
        Assert.Equal(string.Join(Nl, "kiff.answer.v1", "t_1", "ce_1", "reject", "return first", "", "3", At),
            Encoding.UTF8.GetString(HeldAnswers.Message("t_1", "ce_1", new HeldAnswer.NextStep("return first"), 3, At)));
        Assert.Equal(string.Join(Nl, "kiff.answer.v1", "t_1", "ce_1", "change-card", "",
                Convert.ToHexString(SHA256.HashData("a note"u8)).ToLowerInvariant(), "3", At),
            Encoding.UTF8.GetString(HeldAnswers.Message("t_1", "ce_1", new HeldAnswer.ExtraRoom(), 3, At, "a note")));
        Assert.Equal("reject", HeldAnswers.Kiff(new HeldAnswer.Refuse()));
    }

    /// <summary>The routes, escaped, and the bodies: the device key's value, or the passkey's four parts in its place.</summary>
    [Fact]
    public void Routes_and_bodies()
    {
        Assert.Equal("/v1/me/card-exceptions/ce%2F1/approve-once", HeldAnswers.Path("ce/1", new HeldAnswer.ApproveOnce()));
        Assert.Equal("/v1/me/card-exceptions/ce_1/reject", HeldAnswers.Path("ce_1", new HeldAnswer.NextStep("x")));
        Assert.Equal("/v1/me/card-exceptions/ce_1/change-card", HeldAnswers.Path("ce_1", new HeldAnswer.ExtraRoom()));
        Assert.Equal("""{"signature":{"signed_at":"2026-11-15T21:00:00.000Z","value":"MEU="}}""",
            Encoding.UTF8.GetString(HeldAnswers.Body(new HeldAnswer.Refuse(), At, "MEU=")));
        Assert.Equal("""{"next_step":"return first","signature":{"signed_at":"2026-11-15T21:00:00.000Z","value":"MEU="}}""",
            Encoding.UTF8.GetString(HeldAnswers.Body(new HeldAnswer.NextStep("return first"), At, "MEU=")));
        Assert.Equal("""{"signature":{"signed_at":"2026-11-15T21:00:00.000Z","passkey":{"credential_id":"Y3I"}}}""",
            Encoding.UTF8.GetString(HeldAnswers.Body(new HeldAnswer.ApproveOnce(), At, new JsonObject { ["credential_id"] = "Y3I" })));
        Assert.True(HeldAnswers.Grants(new HeldAnswer.ApproveOnce()));
        Assert.True(HeldAnswers.Grants(new HeldAnswer.ExtraRoom()));
        Assert.False(HeldAnswers.Grants(new HeldAnswer.NextStep("x")));
    }

    /// <summary>Answered here with the device key, a tenant and the hold's version; approving with this PC's passkey besides.</summary>
    [Fact]
    public void Who_answers_and_who_approves()
    {
        Assert.True(HeldAnswers.CanAnswer(Call(), "t_1", hasDeviceKey: true));
        Assert.False(HeldAnswers.CanAnswer(Call(version: null), "t_1", true));
        Assert.False(HeldAnswers.CanAnswer(Call(id: null), "t_1", true));
        Assert.False(HeldAnswers.CanAnswer(Call(), null, true));
        Assert.False(HeldAnswers.CanAnswer(Call(), "t_1", false));
        Assert.True(HeldAnswers.Approves(ApprovalMode.Passkey, "Y3I"));
        Assert.False(HeldAnswers.Approves(ApprovalMode.Passkey, null));
        Assert.False(HeldAnswers.Approves(ApprovalMode.View, "Y3I"));
    }

    /// <summary>KIFF's lasting refusals, by error or reason, never hold_check_unavailable; self_review refuses only approving.</summary>
    [Fact]
    public void Lasting_refusals()
    {
        Assert.Equal("self_review", HeldAnswers.LastingRefusal(403, """{"error":"forbidden","reason":"self_review"}"""));
        Assert.Equal("role_forbidden", HeldAnswers.LastingRefusal(403, """{"error":"role_forbidden"}"""));
        Assert.Null(HeldAnswers.LastingRefusal(503, """{"error":"hold_check_unavailable"}"""));
        Assert.Null(HeldAnswers.LastingRefusal(409, """{"error":"stale"}"""));
        Assert.Null(HeldAnswers.LastingRefusal(403, "not json"));
        Assert.True(HeldAnswers.RefusesOnly("self_review"));
        Assert.False(HeldAnswers.RefusesOnly("role_forbidden"));
    }

    /// <summary>The authenticator data a verified Windows Hello assertion for kiff.dev gives: UP and UV, not backup-eligible.</summary>
    static byte[] GoodAuthenticatorData() => [.. SHA256.HashData("kiff.dev"u8), 0x05, 0, 0, 0, 1];

    /// <summary>The passkey's half: the challenge is base64url(sha256(message)); a passkey that can leave this PC is refused.</summary>
    [Fact]
    public async Task The_passkey_s_half()
    {
        var message = HeldAnswers.Message("t_1", "ce_1", new HeldAnswer.ApproveOnce(), 3, At);
        byte[]? asked = null;
        var parts = await HeldAnswers.PasskeyAsync(message, "cr"u8.ToArray(),
            clientData => { asked = clientData; return Task.FromResult(new PasskeyAssertion(GoodAuthenticatorData(), [0x30, 0x06])); });
        Assert.Equal(KiffSigning.Base64Url(SHA256.HashData(message)), JsonDocument.Parse(asked!).RootElement.GetProperty("challenge").GetString());
        Assert.Equal(["credential_id", "client_data_json", "authenticator_data", "signature"], parts.Select(p => p.Key));
        Assert.Equal("Y3I", (string?)parts["credential_id"]);
        Assert.Equal(KiffSigning.Base64Url(asked!), (string?)parts["client_data_json"]);
        byte[] synced = [.. SHA256.HashData("kiff.dev"u8), 0x1D, 0, 0, 0, 1];
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => HeldAnswers.PasskeyAsync(message, "cr"u8.ToArray(),
            _ => Task.FromResult(new PasskeyAssertion(synced, [0x30]))));
    }

    sealed class Kiff
    {
        public (int, string) Answer = (200, "{}");
        public Exception? PasskeyThrows;
        public bool Current = true;
        public ApprovalMode Mode = ApprovalMode.Passkey;
        public string? PasskeyId = "AQI";
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public string? Prompt;
        public readonly List<string> Calls = [];
        public byte[]? Signed;
        public string? Posted;

        public Task<HeldAnswers.Answered> Answer_(HeldAnswer answer, HeldCall? call = null) => HeldAnswers.AnswerAsync(En, call ?? Call(), answer, "t_1",
            Mode, PasskeyId,
            prompt => { Calls.Add("confirm"); Prompt = prompt; return Task.FromResult(Confirmed); },
            message => { Calls.Add("passkey"); Signed = message; return PasskeyThrows is { } e ? Task.FromException<JsonObject>(e) : Task.FromResult(new JsonObject { ["credential_id"] = "Y3I" }); },
            message => { Calls.Add("device key"); Signed = message; return Task.FromResult("MEU="); },
            (path, body) => { Calls.Add("POST " + path); Posted = Encoding.UTF8.GetString(body); return Task.FromResult(Answer); },
            () => { Calls.Add("time"); return At; }, () => Current);
    }

    /// <summary>Approving takes the passkey and refusing the device key, each over its own kiff.answer.v1, then KIFF takes it.</summary>
    [Fact]
    public async Task Each_answer_with_its_own_key()
    {
        var approve = new Kiff();
        Assert.True((await approve.Answer_(new HeldAnswer.ApproveOnce())).Taken);
        Assert.Equal(["time", "passkey", "POST /v1/me/card-exceptions/ce_1/approve-once"], approve.Calls);
        Assert.Equal(HeldAnswers.Message("t_1", "ce_1", new HeldAnswer.ApproveOnce(), 3, At), approve.Signed);
        Assert.Equal("""{"signature":{"signed_at":"2026-11-15T21:00:00.000Z","passkey":{"credential_id":"Y3I"}}}""", approve.Posted);

        var refuse = new Kiff();
        Assert.True((await refuse.Answer_(new HeldAnswer.NextStep("return first"))).Taken);
        Assert.Equal(["confirm", "time", "device key", "POST /v1/me/card-exceptions/ce_1/reject"], refuse.Calls);
        Assert.Equal("""{"next_step":"return first","signature":{"signed_at":"2026-11-15T21:00:00.000Z","value":"MEU="}}""", refuse.Posted);

        var room = new Kiff();
        await room.Answer_(new HeldAnswer.ExtraRoom());
        Assert.Equal(["time", "passkey", "POST /v1/me/card-exceptions/ce_1/change-card"], room.Calls);
    }

    /// <summary>Closed is cancelled, refused is not confirmed: nothing is sent and the call stays.</summary>
    [Fact]
    public async Task Not_confirmed_nothing_is_sent()
    {
        var closed = new Kiff { PasskeyThrows = new OperationCanceledException() };
        Assert.Equal(new HeldAnswers.Answered(false, "Cancelled, so nothing was approved. The request is kept: approve again when you're ready."),
            await closed.Answer_(new HeldAnswer.ApproveOnce()));
        var refused = new Kiff { PasskeyThrows = new PasskeyRefusedException("this passkey can leave this computer") };
        Assert.Equal(new HeldAnswers.Answered(false, "Not sent: it wasn't confirmed."), await refused.Answer_(new HeldAnswer.ExtraRoom()));
        Assert.Null(closed.Posted);
        Assert.Null(refused.Posted);
    }

    /// <summary>
    /// Coach's hold of a version to install isn't approved once: it's installed from its card, as the iPad's answer refuses it.
    /// Nothing is confirmed, signed or sent.
    /// </summary>
    [Fact]
    public async Task Coach_s_install_hold_isn_t_approved_once()
    {
        var kiff = new Kiff();
        var install = Call() with { Install = new VersionInstall("taf_refunds", 3) };
        Assert.Equal(new HeldAnswers.Answered(false, En.Get("needs_you.install_from_card")), await kiff.Answer_(new HeldAnswer.ApproveOnce(), install));
        Assert.Empty(kiff.Calls);
        // Refusing it is still an answer.
        Assert.True((await new Kiff().Answer_(new HeldAnswer.Refuse(), install)).Taken);
    }

    /// <summary>KIFF's install hold, read as the iPad's install(of:coach:): Coach's agent, version_install, a taf and n above 0.</summary>
    [Theory]
    [InlineData("setup", "version_install", "taf_refunds", "3", true)]
    [InlineData("refunds-1", "version_install", "taf_refunds", "3", false)]
    [InlineData("setup", "create_refund", "taf_refunds", "3", false)]
    [InlineData("setup", "version_install", "", "3", false)]
    [InlineData("setup", "version_install", "taf_refunds", "0", false)]
    [InlineData("setup", "version_install", "taf_refunds", "x", false)]
    public void An_install_hold_is_Coach_s_version_install(string subject, string action, string taf, string n, bool install)
    {
        var hold = KiffHeld.Read(JsonDocument.Parse($$"""
            {"exceptions":[{"id":"ce_1","subject":"{{subject}}","action":"{{action}}","entity_id":"e","status":"held","breaches":[],
              "arguments":{"taf":"{{taf}}","n":"{{n}}"},"created_at":"2026-11-15T21:00:00Z","expires_at":"2026-11-15T21:10:00Z"}]}
            """).RootElement)![0];
        Assert.Equal(install, KiffHeld.InstallOf(hold) is not null);
    }

    /// <summary>
    /// A session that doesn't approve with this PC's passkey never grants, whatever the card shows: refused before the time,
    /// any prompt or any send. Refusing is still its to do.
    /// </summary>
    [Theory]
    [InlineData(ApprovalMode.Key, "AQI")]
    [InlineData(ApprovalMode.View, "AQI")]
    [InlineData(ApprovalMode.Passkey, null)]
    public async Task Only_a_passkey_session_grants(ApprovalMode mode, string? passkeyId)
    {
        foreach (var grant in new HeldAnswer[] { new HeldAnswer.ApproveOnce(), new HeldAnswer.ExtraRoom() })
        {
            var kiff = new Kiff { Mode = mode, PasskeyId = passkeyId };
            Assert.Equal(new HeldAnswers.Answered(false, En.Get("needs_you.answer_in_kiff")), await kiff.Answer_(grant));
            Assert.Empty(kiff.Calls);
        }
        Assert.True((await new Kiff { Mode = mode, PasskeyId = passkeyId }.Answer_(new HeldAnswer.Refuse())).Taken);
    }

    /// <summary>
    /// Refusing asks for the person first with the iPad's prompt, then takes the time, then the device key signs, then it's
    /// sent; closed or not confirmed, nothing is signed.
    /// </summary>
    [Fact]
    public async Task A_refusal_is_confirmed_before_the_device_key_signs()
    {
        var refuse = new Kiff();
        Assert.True((await refuse.Answer_(new HeldAnswer.Refuse())).Taken);
        Assert.Equal(["confirm", "time", "device key", "POST /v1/me/card-exceptions/ce_1/reject"], refuse.Calls);
        Assert.Equal("Answer Refund a payment", refuse.Prompt);

        var closed = new Kiff { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal(new HeldAnswers.Answered(false, En.Get("approval.cancelled_nothing_approved_request_kept")), await closed.Answer_(new HeldAnswer.NextStep("return first")));
        var unconfirmed = new Kiff { Confirmed = StepConfirmed.Failed };
        Assert.Equal(new HeldAnswers.Answered(false, En.Get("common.not_sent_unconfirmed")), await unconfirmed.Answer_(new HeldAnswer.Refuse()));
        Assert.Equal(["confirm"], closed.Calls);
        Assert.Equal(["confirm"], unconfirmed.Calls);

        // Approving asks no separate confirmation: the passkey's Windows Hello is it.
        var approve = new Kiff();
        await approve.Answer_(new HeldAnswer.ApproveOnce());
        Assert.Equal(["time", "passkey", "POST /v1/me/card-exceptions/ce_1/approve-once"], approve.Calls);
    }

    /// <summary>The iPad's vectors, verbatim (KIFFAnswerTests.swift): reject with a next step, approve-once, change-card, and a note's hash.</summary>
    [Fact]
    public void The_iPad_s_answer_vectors()
    {
        string M(HeldAnswer a, string at, string? note = null) => Encoding.UTF8.GetString(HeldAnswers.Message("t1", "ce_1", a, 2, at, note));
        Assert.Equal("kiff.answer.v1\nt1\nce_1\nreject\nreturn first\n\n2\n2026-10-14T10:45:00Z", M(new HeldAnswer.NextStep("return first"), "2026-10-14T10:45:00Z"));
        Assert.Equal("kiff.answer.v1\nt1\nce_1\napprove-once\n\n\n2\n2026-10-14T10:45:00Z", M(new HeldAnswer.ApproveOnce(), "2026-10-14T10:45:00Z"));
        Assert.Equal("kiff.answer.v1\nt1\nce_1\nchange-card\n\n\n2\n2026-10-14T10:45:00Z", M(new HeldAnswer.ExtraRoom(), "2026-10-14T10:45:00Z"));
        Assert.Equal("kiff.answer.v1\nt1\nce_1\nreject\nreturn_first\n2b951386ddf8c195c3f5603c65de2156acb7f9f2a13638581448711e019dc654\n2\n2026-10-14T12:00:00Z",
            M(new HeldAnswer.NextStep("return_first"), "2026-10-14T12:00:00Z", "Ask for photos."));
    }

    /// <summary>KIFF's refusal in the app's words, with its lasting code; no hold version answers in KIFF; a sign-out drops it.</summary>
    [Fact]
    public async Task Refusals_and_the_rest()
    {
        var selfReview = await new Kiff { Answer = (403, """{"error":"forbidden","reason":"self_review"}""") }.Answer_(new HeldAnswer.ApproveOnce());
        Assert.Equal(new HeldAnswers.Answered(false, "You asked for this, so someone else on the account approves it. You can still refuse it.", "self_review"), selfReview);
        var down = await new Kiff { Answer = (503, "{}") }.Answer_(new HeldAnswer.Refuse());
        Assert.Equal(new HeldAnswers.Answered(false, "KIFF didn't take the answer: It isn't working on our side right now. Try again later."), down);
        var noVersion = new Kiff();
        Assert.Equal(new HeldAnswers.Answered(false, En.Get("needs_you.answer_in_kiff")), await noVersion.Answer_(new HeldAnswer.Refuse(), Call(version: null)));
        Assert.Empty(noVersion.Calls);
        var signedOut = new Kiff { Current = false };
        Assert.True((await signedOut.Answer_(new HeldAnswer.Refuse())).Stopped);
        Assert.Null(signedOut.Posted);
    }
}
