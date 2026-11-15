using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>
/// The one message tafs cloud asks this device to sign, and the call it goes into, as the iPad's
/// <c>TafsCloud.ApprovalDecision.Ask</c>: its step (<c>connect</c> or <c>cards</c>), the exact message, the prompt's words,
/// planque's approve or KIFF's draft, and which key the call takes (<c>window</c> or <c>critical</c>, a hint).
/// </summary>
public sealed record ApprovalAsk(string Id, string Step, string Message, string Reason, ApprovalPlanque? Planque, ApprovalKiff? Kiff, string? Key);

/// <summary>planque's approve, its body as tafs cloud built it and sent as it is; <paramref name="Digest"/> is the body's own.</summary>
public sealed record ApprovalPlanque(string Grant, string Digest, JsonElement Body);

/// <summary>KIFF's draft to confirm, at exactly this version.</summary>
public sealed record ApprovalKiff(string Draft, string Version);

/// <summary>
/// tafs cloud's decision on an approval card, as the iPad's <c>TafsCloud.ApprovalDecision</c>: <c>answered</c> (Coach heard
/// <paramref name="Answer"/>; <paramref name="Note"/> is the card's line), <c>read_again</c>, or <c>sign</c> with its ask.
/// </summary>
public sealed record ApprovalDecision(string Outcome, string? Answer, string? Note, ApprovalAsk? Ask, string? Session, string? Run);

/// <summary>
/// Approve on Coach's approval card, as the iPad's <c>AppModel.approve</c>: what's asked of tafs cloud, what this device
/// may sign, and the signed report of what came of the call. tafs cloud answers Coach; this device never builds the answer.
/// </summary>
public static class CoachApproval
{
    /// <summary>tafs cloud's code when a sign-in planque holds isn't confirmed yet, as the iPad's <c>signInNotConfirmed</c>.</summary>
    public const string SignInNotConfirmed = "sign_in_not_confirmed";

    static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Approve tapped, as the iPad's <c>TafsCloud.approval</c>: <c>POST /v1/tafs/{coach}/components/{component}/approval</c>.</summary>
    public static string Path(string coach, string component) =>
        $"/v1/tafs/{CloudRead.PathPart(coach)}/components/{CloudRead.PathPart(component)}/approval";

    /// <summary>
    /// What the card showed, as the iPad's <c>ApprovalAskBody</c>: the grant, the digest shown, the draft and the version shown,
    /// and the conversation. A field with no value is left out, as Swift's encoder leaves out a nil.
    /// </summary>
    public static byte[] AskBody(string grant, string? shownDigest, string? draft, string? shownDraft, string? session) =>
        Encode(("grant", grant), ("shown_digest", shownDigest), ("draft", draft), ("shown_draft", shownDraft), ("session", session));

    /// <summary>What came of the call, as the iPad's <c>ApprovalReportBody</c>: never an answer, only what happened.</summary>
    public static byte[] ReportBody(string result, string? status, string? why, string? session) =>
        Encode(("result", result), ("status", status), ("why", why), ("session", session));

    static byte[] Encode(params (string Name, string? Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (name, value) in fields)
            if (value is not null) o[name] = value;
        return Encoding.UTF8.GetBytes(o.ToJsonString(Plain));
    }

    /// <summary>The report's path, as the iPad's <c>approvalReport</c>: <c>…/approval/{ask}</c>.</summary>
    public static string ReportPath(string coach, string component, string ask) => Path(coach, component) + "/" + CloudRead.PathPart(ask);

    /// <summary>The report signed as a setup call, as the iPad's <c>approvalReport</c>: <c>kiff.request.v1</c> over exactly the body sent.</summary>
    public static byte[] ReportMessage(string tenant, string path, byte[] body, string signedAt) =>
        KiffSigning.RequestV1(tenant, "POST", path, body, signedAt);

    /// <summary>The report's <c>KIFF-Device-Signature</c>: <c>t={signedAt},v1={base64 DER}</c>.</summary>
    public static string ReportSignature(string signedAt, byte[] derSignature) => KiffSigning.SignatureHeaderV1(signedAt, derSignature);

    /// <summary>
    /// tafs cloud's decision, read as strictly as the iPad's decoder: <c>outcome</c> is required, and an ask its id, step,
    /// message and reason, planque's grant and body (with its digest), KIFF's draft and version. A field of another shape
    /// fails it, except <c>key</c>, a hint read leniently: another shape is none.
    /// </summary>
    public static ApprovalDecision? Read(JsonElement d)
    {
        if (!Shape.Required(d, "outcome", Shape.Text) || !((string[])["answer", "note", "session", "run"]).All(n => Shape.Optional(d, n, Shape.Text))
            || !Shape.Optional(d, "ask", IsAsk)) return null;
        return new ApprovalDecision(Lenient.Text(d, "outcome")!, Lenient.Text(d, "answer"), Lenient.Text(d, "note"),
            Lenient.Field(d, "ask") is { ValueKind: JsonValueKind.Object } ask ? Ask(ask) : null, Lenient.Text(d, "session"), Lenient.Text(d, "run"));
    }

    static bool IsAsk(JsonElement a) =>
        ((string[])["id", "step", "message", "reason"]).All(n => Shape.Required(a, n, Shape.Text))
        && Shape.Optional(a, "planque", p => Shape.Required(p, "grant", Shape.Text) && Shape.Required(p, "body", IsApproveBody))
        && Shape.Optional(a, "kiff", k => Shape.Required(k, "draft", Shape.Text) && Shape.Required(k, "version", Shape.Text));

    /// <summary>planque's approve body as the iPad's <c>PlanqueClient.ApproveBody</c>: its digest, and KIFF's profile with both its hashes.</summary>
    static bool IsApproveBody(JsonElement b) =>
        Shape.Required(b, "digest", Shape.Text)
        && Shape.Optional(b, "kiff_profile", p => Shape.Required(p, "profile_sha256", Shape.Text) && Shape.Required(p, "words_sha256", Shape.Text));

    static ApprovalAsk Ask(JsonElement a)
    {
        string T(JsonElement o, string n) => Lenient.Text(o, n)!;
        var planque = Lenient.Field(a, "planque") is { ValueKind: JsonValueKind.Object } p
            ? new ApprovalPlanque(T(p, "grant"), T(p.GetProperty("body"), "digest"), p.GetProperty("body").Clone()) : null;
        var kiff = Lenient.Field(a, "kiff") is { ValueKind: JsonValueKind.Object } k ? new ApprovalKiff(T(k, "draft"), T(k, "version")) : null;
        return new ApprovalAsk(T(a, "id"), T(a, "step"), T(a, "message"), T(a, "reason"), planque, kiff, Lenient.Text(a, "key"));
    }

    /// <summary>
    /// Whether the step asked signs the Card as the card's mark said, as the iPad's <c>stepMatchesMark</c>: <c>card: true</c>
    /// for <c>cards</c>, <c>key_and_cards</c> or <c>tools_and_cards</c>, <c>card: false</c> for any other. No mark: any.
    /// </summary>
    public static bool StepMatchesMark(CardSigns? signs, string step) =>
        signs?.Card is not { } card || card == (step is "cards" or "key_and_cards" or "tools_and_cards");

    /// <summary>
    /// The message this card's approval may sign, rebuilt from what this device holds, as the iPad's
    /// <c>expectedApprovalMessage</c>: <c>tafs.connect.v1|{tenant}|{grant}|{digest shown}</c> or
    /// <c>tafs.cards.v1|{tenant}|{draft}|{version shown}</c>. None for an ask that names another grant, draft, digest or
    /// version, a step this app doesn't know, or when nothing was shown.
    /// </summary>
    public static string? ExpectedMessage(ApprovalAsk ask, string? grant, string? draft, string tenant, string? shownDigest, string? shownDraft) =>
        ask.Step switch
        {
            // The digest is the one shown, never the ask's: terms never read are never signed.
            "connect" when ask.Planque is { } p && grant is not null && p.Grant == grant && shownDigest is not null && p.Digest == shownDigest
                => $"tafs.connect.v1|{tenant}|{grant}|{shownDigest}",
            "cards" when ask.Kiff is { } k && draft is not null && k.Draft == draft && shownDraft is not null && k.Version == shownDraft
                => $"tafs.cards.v1|{tenant}|{draft}|{shownDraft}",
            _ => null,
        };

    /// <summary>KIFF's confirm of the draft the ask names, at its version, as the iPad's <c>KIFFClient.confirmDraft(id:version:)</c>.</summary>
    public static (string Path, byte[] Body) ConfirmRequest(ApprovalKiff kiff) =>
        (KiffDrafts.Path(kiff.Draft) + "/confirm", Encoding.UTF8.GetBytes(new JsonObject { ["version"] = kiff.Version }.ToJsonString()));

    /// <summary>
    /// Approve on a Coach card, as the iPad's <c>approve</c>: tafs cloud decides. This PC sends that Approve was tapped, with
    /// what the card showed (the grant, the digest and the draft's version), and planque's read proof. With nothing to sign,
    /// tafs cloud answers Coach itself and the card shows its note. Asked to sign the Card (<c>cards</c>), this PC rebuilds
    /// the message from what it holds and refuses any other; one Windows Hello, this PC's passkey asserting KIFF's confirm
    /// itself, signs and sends it; then it reports what came of it, signed by its device key with a fresh read proof, and
    /// shows tafs cloud's note. The other steps (connecting an app, its tools with the Card, a key) aren't on this PC yet.
    /// Windows Hello closed sends nothing and says nothing.
    /// </summary>
    public static async Task<ApproveResult> ApproveAsync(WordBook w, string coach, CoachCard card, string tenant, string? session,
        string? shownDigest, string? shownDraft, ApproveCalls calls)
    {
        if (card.Grant is not { } grant) return new(null);
        var proof = await calls.Proof();
        if (!calls.Current()) return ApproveResult.Stopped;
        var asked = await calls.Ask(Path(coach, card.Id), AskBody(grant, shownDigest, card.Draft, shownDraft, session), proof);
        if (!calls.Current()) return ApproveResult.Stopped;
        if (Decided(w, asked) is not { } decision) return NotReached(w, asked);
        switch (decision.Outcome)
        {
            case "answered":
                return new(decision.Note, ReadAgain: true);
            case "read_again":
                return new(decision.Note ?? w.Get("version.out_of_date_card"), ReadAgain: true);
            case "sign":
                break;
            default:
                return new(w.Get("coach.unknown_answer"));
        }
        if (decision.Ask is not { } ask) return new(null);
        // What the card said this signs must be what it signs now: else it's out of date, and read again.
        if (!StepMatchesMark(card.Signs, ask.Step)) return new(w.Get("version.out_of_date_card"), ReadAgain: true);
        if (ask.Step != "cards" || ask.Kiff is not { } kiff) return new(w.Get("coach.unknown_answer"));
        // The last check, not a signing service: only the message this card implies, rebuilt from what this PC holds.
        if (ask.Message != ExpectedMessage(ask, grant, card.Draft, tenant, shownDigest, shownDraft)) return new(w.Get("coach.ask_not_this_cards"));

        var (path, body) = ConfirmRequest(kiff);
        (string Header, string Value) signature;
        try { signature = await calls.SignConfirm(path, body); }
        catch (OperationCanceledException) { return calls.Current() ? new(null) : ApproveResult.Stopped; }
        catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or System.Runtime.InteropServices.COMException
            or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return calls.Current() ? new(e is PasskeyRefusedException ? e.Message : calls.Why(e)) : ApproveResult.Stopped;
        }
        if (!calls.Current()) return ApproveResult.Stopped;
        // What came of it, as the report says it: never an answer, only what happened.
        string result = "confirmed";
        string? why = null;
        try
        {
            var (status, answer) = await calls.Confirm(path, body, signature);
            if (status is < 200 or > 299) (result, why) = ("failed", CoachStart.KiffRefused(w, status, answer));
        }
        catch (System.Net.Http.HttpRequestException) { (result, why) = ("failed", w.Get("error.offline")); }
        if (!calls.Current()) return ApproveResult.Stopped;

        // A fresh read proof: planque takes each signed request once.
        var reportProof = await calls.Proof();
        if (!calls.Current()) return ApproveResult.Stopped;
        var reportPath = ReportPath(coach, card.Id, ask.Id);
        var report = ReportBody(result, null, why, session);
        var signedAt = calls.SignedAt();
        string reportSignature;
        try { reportSignature = ReportSignature(signedAt, await calls.SignReport(ReportMessage(tenant, reportPath, report, signedAt))); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            return calls.Current() ? new(w.Get("coach.answer_not_reached", ("why", calls.Why(e))), ReadAgain: true) : ApproveResult.Stopped;
        }
        var reported = await calls.Report(reportPath, report, reportSignature, reportProof);
        if (!calls.Current()) return ApproveResult.Stopped;
        return Decided(w, reported) is { } outcome ? new(outcome.Note, ReadAgain: true) : NotReached(w, reported);
    }

    static ApprovalDecision? Decided(WordBook w, CloudRead r) => r.Answer is { } json ? Read(json) : null;

    /// <summary>
    /// tafs cloud's answer didn't come, or can't be read: its refusal, else that the answer didn't reach Setup, and the thread
    /// is read again. A sign-in planque holds that isn't confirmed yet (<see cref="SignInNotConfirmed"/>) says tafs cloud's
    /// own line, as it is.
    /// </summary>
    static ApproveResult NotReached(WordBook w, CloudRead r) =>
        r.Code == SignInNotConfirmed && r.Why is { } line ? new(line, ReadAgain: true)
        : new(w.Get("coach.answer_not_reached", ("why", r.Answer is null ? r.Why ?? "" : w.Get("kiff.kiff_did_not_answer_expected"))), ReadAgain: true);
}

/// <summary>
/// What Approve sends, given by the app and faked in tests: planque's read proof (none when it can't be made), tafs cloud's
/// ask (path, body, proof), the passkey's header for KIFF's confirm (it throws <see cref="OperationCanceledException"/>
/// when Windows Hello is closed), the confirm (it throws <see cref="System.Net.Http.HttpRequestException"/> when not
/// reached), the device key's DER signature over the report's message, the report (path, body, its
/// <c>KIFF-Device-Signature</c>, proof), <see cref="SigningTime.Next"/>'s time, whether the session Approve began in is still
/// signed in, and an error's code in words.
/// </summary>
public sealed record ApproveCalls(
    Func<Task<string?>> Proof,
    Func<string, byte[], string?, Task<CloudRead>> Ask,
    Func<string, byte[], Task<(string Header, string Value)>> SignConfirm,
    Func<string, byte[], (string Header, string Value), Task<(int Status, string Body)>> Confirm,
    Func<byte[], Task<byte[]>> SignReport,
    Func<string, byte[], string, string?, Task<CloudRead>> Report,
    Func<string> SignedAt,
    Func<bool> Current,
    Func<Exception, string> Why);

/// <summary>What Approve came to: the card's note (none to say nothing), and whether the thread is read again.</summary>
public sealed record ApproveResult(string? Note, bool ReadAgain = false, bool Stop = false)
{
    /// <summary>Another session began meanwhile: nothing more was sent, and nothing is shown.</summary>
    public static readonly ApproveResult Stopped = new(null, Stop: true);
}
