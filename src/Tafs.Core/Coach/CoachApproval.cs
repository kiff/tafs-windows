using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

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
}
