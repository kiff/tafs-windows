using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>An answer to a held call (the iPad's <c>Answer</c>): approve it once, refuse it, refuse it with a Card's next step, or extra room.</summary>
public abstract record HeldAnswer
{
    public sealed record ApproveOnce : HeldAnswer;
    public sealed record Refuse : HeldAnswer;
    /// <param name="Step">One of the Card's next steps ("return first").</param>
    public sealed record NextStep(string Step) : HeldAnswer;
    /// <summary>The Card's extra room for this call, until its total resets (KIFF's change-card).</summary>
    public sealed record ExtraRoom : HeldAnswer;
}

/// <summary>
/// Answering a held call, as the iPad's <c>AppModel.answer(_:with:)</c>, <c>kiffAnswer</c>, <c>canAnswer</c> and
/// <c>KIFFClient.answerMessage</c>/<c>approveOnce</c>/<c>reject</c>/<c>changeCard</c>, signed as the desktop spec says (§2.5,
/// §2.6): a passkey session approves once and gives extra room with a fresh passkey assertion over the answer's
/// <c>kiff.answer.v1</c>; a refusal, with a next step or not, is signed by the device key.
/// </summary>
public static class HeldAnswers
{
    /// <summary>The answer as <c>kiff.answer.v1</c> names it: approve-once, reject (a next step too) or change-card.</summary>
    public static string Kiff(HeldAnswer answer) => answer switch
    {
        HeldAnswer.ApproveOnce => "approve-once",
        HeldAnswer.ExtraRoom => "change-card",
        _ => "reject",
    };

    /// <summary>Approving, or giving the Card room, grants authority: a passkey assertion signs it. A refusal grants nothing.</summary>
    public static bool Grants(HeldAnswer answer) => answer is HeldAnswer.ApproveOnce or HeldAnswer.ExtraRoom;

    public static string? Step(HeldAnswer answer) => answer is HeldAnswer.NextStep n ? n.Step : null;

    /// <summary><c>/v1/me/card-exceptions/{id}/approve-once</c>, <c>…/reject</c> or <c>…/change-card</c>.</summary>
    public static string Path(string kiffId, HeldAnswer answer) => $"/v1/me/card-exceptions/{CloudRead.PathPart(kiffId)}/{Kiff(answer)}";

    /// <summary>
    /// KIFF's <c>kiff.answer.v1</c>, one field per line: tenant, the hold's id, the answer, the next step or empty, sha256 of
    /// the note in hex or empty, KIFF's accounting version for the hold, and signed-at.
    /// </summary>
    public static byte[] Message(string tenant, string kiffId, HeldAnswer answer, int accountingVersion, string signedAt, string? note = null) =>
        KiffSigning.Message("kiff.answer.v1", tenant, kiffId, Kiff(answer), Step(answer) ?? "",
            string.IsNullOrEmpty(note) ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(note))).ToLowerInvariant(),
            accountingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), signedAt);

    /// <summary>The body with the device key's signature: <c>{"next_step"?, "signature": {"signed_at", "value"}}</c>, the value base64 DER.</summary>
    public static byte[] Body(HeldAnswer answer, string signedAt, string value) =>
        BodyWith(answer, new JsonObject { ["signed_at"] = signedAt, ["value"] = value });

    /// <summary>
    /// The body with a passkey assertion in place of <c>value</c> (spec §2.6): <c>{"signature": {"signed_at", "passkey":
    /// {"credential_id", "client_data_json", "authenticator_data", "signature"}}}</c>, its challenge base64url(sha256) of the
    /// answer's message.
    /// </summary>
    public static byte[] Body(HeldAnswer answer, string signedAt, JsonObject passkey) =>
        BodyWith(answer, new JsonObject { ["signed_at"] = signedAt, ["passkey"] = passkey });

    static byte[] BodyWith(HeldAnswer answer, JsonObject signature)
    {
        var body = new JsonObject();
        if (Step(answer) is { } step) body["next_step"] = step;
        body["signature"] = signature;
        return Encoding.UTF8.GetBytes(body.ToJsonString());
    }

    /// <summary>
    /// Whether this PC can answer a held call from KIFF (the iPad's <c>canAnswer</c>): KIFF checks its signature, so the session
    /// holds its device key, the tenant is known and the hold says which version of it the person saw. Approving and extra
    /// room take this PC's passkey besides (a passkey session); without it, only refusing is answered here.
    /// </summary>
    public static bool CanAnswer(HeldCall call, string? tenant, bool hasDeviceKey) =>
        call.KiffId is not null && tenant is not null && hasDeviceKey && call.AccountingVersion is not null;

    /// <summary>
    /// The passkey's half of an answer that grants (spec §2.6): one fresh assertion with this PC's passkey, its challenge
    /// base64url(sha256) of the answer's <c>kiff.answer.v1</c>, checked as <see cref="Approvals.PasskeyApprover"/> checks an
    /// approval's (kiff.dev, Windows Hello verified the person, a passkey that can't leave this PC). Closing Windows Hello
    /// throws <see cref="OperationCanceledException"/>; anything KIFF would refuse, <see cref="Approvals.PasskeyRefusedException"/>.
    /// </summary>
    /// <param name="assert">The WebAuthn call over the client data's bytes, which shows Windows Hello.</param>
    public static async Task<JsonObject> PasskeyAsync(byte[] message, byte[] credentialId, Func<byte[], Task<Approvals.PasskeyAssertion>> assert)
    {
        if (!Approvals.Features.WindowsPasskeyApprovals) throw new Approvals.PasskeyRefusedException("passkey approvals are off on Windows");
        var clientData = Approvals.PasskeyApprover.ClientDataJson(message);
        var assertion = await assert(clientData);
        if (Approvals.PasskeyApprover.Refusal(Approvals.AuthenticatorData.Parse(assertion.AuthenticatorData)) is { } refusal)
            throw new Approvals.PasskeyRefusedException(refusal);
        return new JsonObject
        {
            ["credential_id"] = KiffSigning.Base64Url(credentialId),
            ["client_data_json"] = KiffSigning.Base64Url(clientData),
            ["authenticator_data"] = KiffSigning.Base64Url(assertion.AuthenticatorData),
            ["signature"] = KiffSigning.Base64Url(assertion.SignatureDer),
        };
    }

    /// <summary>
    /// KIFF's reason this person may never answer the call, which stays so (the iPad's <c>lastingRefusal</c>): its <c>error</c>,
    /// else its <c>reason</c>, among the refusals the app words, never <c>hold_check_unavailable</c>, which a moment later
    /// could take.
    /// </summary>
    public static string? LastingRefusal(int status, string body)
    {
        if (status is >= 200 and <= 299 or 0 or 401) return null;
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(body).RootElement;
            foreach (var name in new[] { "error", "reason" })
                if (json.ValueKind == System.Text.Json.JsonValueKind.Object && json.TryGetProperty(name, out var v)
                    && v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() is { } code
                    && code != "hold_check_unavailable" && Refusals.Contains(code))
                    return code;
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    static readonly string[] Refusals = ["self_review", "separation_of_duties_role", "separation_of_duties_key", "card_changer", "role_forbidden"];

    /// <summary>KIFF refuses only this person's approval (they asked for the call): they may still refuse it (the iPad's <c>refusesOnly</c>).</summary>
    public static bool RefusesOnly(string code) => code == "self_review";

    /// <summary>How an answer went: taken, not taken with why (and KIFF's lasting refusal, if it was one), or dropped by a sign-out.</summary>
    public sealed record Answered(bool Taken, string? Why = null, string? Lasting = null, bool Stopped = false);

    /// <summary>
    /// Answers a held call from KIFF, as the iPad's <c>answer(_:with:)</c>: KIFF's <c>kiff.answer.v1</c> for this hold, this
    /// answer, the hold's version and now, signed by this PC's passkey for an answer that grants, by its device key for a
    /// refusal, then posted. Closed, it's cancelled and the call stays; not confirmed, it says so; refused, KIFF's reason in
    /// the app's words. Each step stops once another session began.
    /// </summary>
    /// <param name="mode">The session's approval mode, with <paramref name="passkeyId"/>: only a passkey session that knows its
    /// passkey grants (<see cref="Approves"/>); any other is refused before anything is asked or sent.</param>
    /// <param name="confirm">Windows Hello over the iPad's prompt, before the device key signs a refusal.</param>
    /// <param name="passkey">The passkey's half over the message (Windows Hello), for an answer that grants.</param>
    /// <param name="deviceSign">The device key's DER signature over the message, base64, for a refusal.</param>
    /// <param name="signedAt"><see cref="Kiff.SigningTime.Next"/>'s time, as written (RFC 3339 with milliseconds, never the same
    /// twice), taken once the person confirmed.</param>
    public static async Task<Answered> AnswerAsync(WordBook w, HeldCall call, HeldAnswer answer, string tenant, ApprovalMode mode, string? passkeyId,
        Func<string, Task<StepConfirmed>> confirm, Func<byte[], Task<JsonObject>> passkey, Func<byte[], Task<string>> deviceSign,
        Func<string, byte[], Task<(int Status, string Body)>> post, Func<string> signedAt, Func<bool> current)
    {
        if (call.KiffId is not { } id || call.AccountingVersion is not { } version) return new(false, w.Get("needs_you.answer_in_kiff"));
        // Coach's version goes live from its card, never by approving its hold once, as the iPad's answer refuses it.
        if (answer is HeldAnswer.ApproveOnce && call.Install is not null) return new(false, w.Get("needs_you.install_from_card"));
        // Only a session that approves with this PC's passkey grants: any other is refused here, whatever the card shows.
        if (Grants(answer) && !Approves(mode, passkeyId)) return new(false, w.Get("needs_you.answer_in_kiff"));
        if (!Grants(answer))
        {
            // The device key's answer asks for the person first, as the iPad's signer.sign(message, reason:) asks Face ID.
            var confirmed = await confirm(w.Get("needs_you.prompt.answer", ("summary", call.Summary)));
            if (!current()) return new(false, Stopped: true);
            if (confirmed != StepConfirmed.Yes)
                return new(false, w.Get(confirmed == StepConfirmed.Cancelled ? "approval.cancelled_nothing_approved_request_kept" : "common.not_sent_unconfirmed"));
        }
        // signed_at once the person confirmed: a slow confirmation never ages the answer KIFF checks for skew.
        var at = signedAt();
        var message = Message(tenant, id, answer, version, at);
        byte[] body;
        try
        {
            body = Grants(answer) ? Body(answer, at, await passkey(message)) : Body(answer, at, await deviceSign(message));
        }
        catch (OperationCanceledException)
        {
            return current() ? new(false, w.Get("approval.cancelled_nothing_approved_request_kept")) : new(false, Stopped: true);
        }
        // Authenticator data too short to read (InvalidDataException) and Windows Hello's own failures (COMException) too:
        // the card says nothing was sent, never waits with no words.
        catch (Exception e) when (e is Approvals.PasskeyRefusedException or CryptographicException or InvalidOperationException
            or InvalidDataException or System.Runtime.InteropServices.COMException)
        {
            return current() ? new(false, w.Get("common.not_sent_unconfirmed")) : new(false, Stopped: true);
        }
        if (!current()) return new(false, Stopped: true);
        var (status, answerBody) = await post(Path(id, answer), body);
        if (!current()) return new(false, Stopped: true);
        if (status is >= 200 and <= 299) return new(true);
        return new(false, YourSteps.RefusedWords(w, status, answerBody), LastingRefusal(status, answerBody));
    }

    /// <summary>The held call's page in KIFF, where it's answered while this PC can't (the iPad's <c>answerPage</c>), in the app's language.</summary>
    public static Uri AnswerPage(string kiffId, string language) =>
        new($"https://app.kiff.dev/needs-you/{CloudRead.PathPart(kiffId)}?lang={CloudRead.QueryPart(language)}");

    /// <summary>Whether this PC approves (approve once, extra room): a passkey session with this PC's own passkey.</summary>
    public static bool Approves(ApprovalMode mode, string? passkeyId) => mode == ApprovalMode.Passkey && passkeyId is not null;
}
