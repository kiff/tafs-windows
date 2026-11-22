using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>Every word on a step is tafs cloud's, in the person's language; <c>Open</c> comes only with a vouched link.</summary>
public sealed record StepWords(string Title = "", string Done = "", string Skip = "", string? Open = null, string Line = "");

/// <summary>
/// "Your step" (docs/api.md › Your step), as the iPad's <c>TafsCloud.YourStep</c>: a step a taf may never take itself, such as
/// approving a bank transfer, which it prepared and handed to the person. They answer Done or Skip to KIFF, signed with
/// this PC's device key; tafs cloud only shows it.
/// </summary>
/// <param name="Handoff">KIFF's hand-off id: what the answer names and signs.</param>
/// <param name="TafName">Home's steps name their taf; null when tafs cloud didn't.</param>
public sealed record YourStep(string Handoff, string Run = "", string? TafId = null, string? TafName = null, string What = "", string Why = "",
    string? Link = null, string? LinkHost = null, string Since = "", string Expires = "", StepWords? Words = null)
{
    public StepWords Said => Words ?? new StepWords();
}

/// <summary>A hand-off as KIFF keeps it (the iPad's <c>KIFFClient.Handoff</c>): its version is what's signed.</summary>
public sealed record Handoff(string Id, int Version = 1, string Status = "")
{
    /// <summary>KIFF still waits on it: any other status means it was answered or has ended.</summary>
    public bool Waiting => Status is "" or "waiting";
}

/// <summary>How answering a step went, as the iPad's <c>answerYourStep</c> leaves it.</summary>
public enum StepAnswered
{
    /// <summary>KIFF took it: the step leaves the list and Home is read again.</summary>
    Answered,
    /// <summary>Answered meanwhile or ended: the step leaves the list, saying so.</summary>
    Ended,
    /// <summary>Not answered, with why: the step stays.</summary>
    NotAnswered,
    /// <summary>Another session began meanwhile: nothing more is done or said.</summary>
    Stopped,
}

/// <summary>The person's confirmation, before the device key signs: given, closed, or failed (Windows Hello not set up, say).</summary>
public enum StepConfirmed { Yes, Cancelled, Failed }

/// <summary>
/// Whether Windows Hello can confirm on this PC, as the iPad's <c>ApprovalReadiness</c>: ready; not set up for this person
/// (Settings opens it); busy just now; or not here at all (no Hello this PC can use, or turned off by policy).
/// </summary>
public enum HelloReadiness { Ready, NotSetUp, Busy, Unsupported }

/// <summary>What a step says in place of Done and Skip while Hello isn't ready, as the iPad's <c>ApprovalNotReady</c>.</summary>
public sealed record HelloNotReady(string Line, bool OpensSettings, bool ChecksAgain);

/// <summary>
/// Reading and answering Your step, as the iPad's <c>YourStep</c>, <c>KIFFClient.handoffMessage</c>/<c>handoff</c>/<c>answerHandoff</c>
/// and <c>AppModel.showSteps</c>/<c>canAnswerSteps</c>/<c>answerYourStep</c>/<c>answerRefusedWords</c>.
/// </summary>
public static class YourSteps
{
    /// <summary>
    /// A step's answer is confirmed with Windows Hello: until Hello is ready, the shared line ("Set up Windows Hello (a PIN is
    /// enough) to answer here, or answer on your phone."), with Open Settings only where setting it up is the way, and Check
    /// again unless this PC has no Hello it can use. Ready, none: Done and Skip.
    /// </summary>
    public static HelloNotReady? NotReady(WordBook w, HelloReadiness hello) => hello == HelloReadiness.Ready ? null
        : new(w.Get("needs_you.your_step.set_up_hello"), hello == HelloReadiness.NotSetUp, hello != HelloReadiness.Unsupported);

    /// <summary>Where Open Settings goes: Windows' sign-in options, where Hello's PIN, face and fingerprint are set up.</summary>
    public static readonly Uri HelloSettings = new("ms-settings:signinoptions");

    public static string HandoffPath(string id) => "/v1/me/handoffs/" + CloudRead.PathPart(id);
    public static string AnswerPath(string id) => HandoffPath(id) + "/answer";

    /// <summary>
    /// Home's <c>your_steps</c>, as the iPad decodes them: a step needs its hand-off; every other field reads leniently with its
    /// default (a taf needs its id). None or null is none. A step without its hand-off makes the list unreadable (null): the
    /// iPad's Home then fails as a whole; here Home's own lines stay, and the steps stay as they were.
    /// </summary>
    public static IReadOnlyList<YourStep>? Read(JsonElement home)
    {
        if (home.ValueKind != JsonValueKind.Object || !home.TryGetProperty("your_steps", out var list) || list.ValueKind == JsonValueKind.Null) return [];
        if (list.ValueKind != JsonValueKind.Array) return null;
        var steps = new List<YourStep>();
        foreach (var s in list.EnumerateArray())
        {
            if (ReadOne(s) is not { } step) return null;
            steps.Add(step);
        }
        return steps;
    }

    /// <summary>
    /// One step, as Home's <c>your_steps[]</c> and a work request's <c>your_step</c> carry it (the iPad's <c>YourStep</c>); null
    /// for one with no hand-off id.
    /// </summary>
    public static YourStep? ReadOne(JsonElement s)
    {
        if (s.ValueKind != JsonValueKind.Object || Text(s, "handoff") is not { } handoff) return null;
        string? tafId = null, tafName = null;
        if (Field(s, "taf") is { ValueKind: JsonValueKind.Object } t && Text(t, "id") is { } id) (tafId, tafName) = (id, Text(t, "name") ?? "");
        var words = Field(s, "words") is { ValueKind: JsonValueKind.Object } w
            ? new StepWords(Text(w, "title") ?? "", Text(w, "done") ?? "", Text(w, "skip") ?? "", Text(w, "open"), Text(w, "line") ?? "")
            : new StepWords();
        return new(handoff, Text(s, "run") ?? "", tafId, tafName, Text(s, "what") ?? "", Text(s, "why") ?? "",
            Text(s, "link"), Text(s, "link_host"), Text(s, "since") ?? "", Text(s, "expires") ?? "", words);
    }

    /// <summary>
    /// The vendor's page, only as tafs cloud vouched for it (the iPad's <c>openLink</c>): an https link with its host apart and
    /// the button's words. The host is tafs cloud's, never parsed here, and must be the link's own, so the line under the
    /// button names where it really goes. Anything else is no button, only the words.
    /// </summary>
    public static (Uri Url, string Host, string Words)? OpenLink(YourStep step)
    {
        if (step.Link is not { } link || step.LinkHost is not { Length: > 0 } host || step.Said.Open is not { Length: > 0 } words
            || !Uri.TryCreate(link, UriKind.Absolute, out var url) || !string.Equals(url.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(url.Host, host, StringComparison.OrdinalIgnoreCase))
            return null;
        return (url, host, words);
    }

    /// <summary>Home's steps, less those answered here that it still lists; an answered step Home no longer lists is forgotten.</summary>
    public static IReadOnlyList<YourStep> Shown(IReadOnlyList<YourStep> listed, HashSet<string> answered)
    {
        answered.IntersectWith(listed.Select(s => s.Handoff));
        return listed.Where(s => !answered.Contains(s.Handoff)).ToList();
    }

    /// <summary>
    /// Whether this PC can answer a step (the iPad's <c>canAnswerSteps</c>): KIFF checks the device key's signature, so the
    /// session must hold it, and the person must answer the account's holds.
    /// </summary>
    public static bool CanAnswer(bool kiff, bool mayAnswerHolds, string? tenant, bool hasKey) => kiff && mayAnswerHolds && tenant is not null && hasKey;

    /// <summary>KIFF's <c>kiff.handoff.v1</c>, one field per line: what the device key signs. Pinned by KIFF's vector.</summary>
    public static byte[] Message(string tenant, string handoff, string answer, int version, string signedAt) =>
        KiffSigning.Message("kiff.handoff.v1", tenant, handoff, answer, version.ToString(System.Globalization.CultureInfo.InvariantCulture), signedAt);

    /// <summary><c>{"answer": "done", "signature": {"signed_at": …, "value": …}}</c>, the value base64 of the DER signature.</summary>
    public static byte[] AnswerBody(string answer, string signedAt, string value) =>
        System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["answer"] = answer,
            ["signature"] = new JsonObject { ["signed_at"] = signedAt, ["value"] = value },
        }.ToJsonString());

    /// <summary>The prompt, in tafs cloud's words for the answer and the step: "Done · Approve the transfer to Acme".</summary>
    public static string Prompt(YourStep step, bool done) =>
        string.Join(" · ", new[] { done ? step.Said.Done : step.Said.Skip, step.What }.Where(x => x.Length > 0));

    /// <summary>A hand-off as KIFF answers it: its id required, version 1 and status "" when not said.</summary>
    public static Handoff? ReadHandoff(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && Text(e, "id") is { } id
            ? new Handoff(id, Field(e, "version") is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : 1, Text(e, "status") ?? "")
            : null;

    /// <summary>
    /// Why KIFF didn't take an answer, in words, never its code (the iPad's <c>answerRefusedWords</c>): KIFF's <c>error</c>,
    /// else its <c>reason</c>, among its reasons in the app's words; else "KIFF didn't take the answer:" with the app's line:
    /// signed out of KIFF for a 401 (the iPad's <c>.unauthorized</c>), not reached, or the status's line.
    /// </summary>
    public static string RefusedWords(WordBook w, int status, string body)
    {
        if (status == 0) return Other(w, w.Get("error.offline"));
        if (status == 401) return Other(w, w.Get("error.signed_out"));
        JsonElement json = default;
        try { json = JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { }
        if ((Reason(w, Text(json, "error")) ?? Reason(w, Text(json, "reason"))) is { } words) return words;
        return Other(w, Plain(w, status));
    }

    static string Other(WordBook w, string why) => w.Get("needs_you.refused.other", ("why", why));

    /// <summary>KIFF's reasons a person may not answer, in words (the iPad's <c>refusalWords</c>); null for any other code.</summary>
    public static string? RefusalWords(WordBook w, string code) => Reason(w, code);

    static string? Reason(WordBook w, string? code) => code switch
    {
        "self_review" => w.Get("needs_you.refused.self_review"),
        "separation_of_duties_role" => w.Get("needs_you.refused.approver_only"),
        "separation_of_duties_key" => w.Get("needs_you.refused.signed_in_approver"),
        "card_changer" => w.Get("needs_you.refused.card_changer"),
        "hold_check_unavailable" => w.Get("needs_you.refused.check_unavailable"),
        "role_forbidden" => w.Get("needs_you.refused.role"),
        _ => null,
    };

    /// <summary>The app's line for a status, never KIFF's words (the iPad's <c>PlainError.words</c>).</summary>
    public static string Plain(WordBook w, int status) => w.Get(status switch
    {
        0 => "error.offline",
        401 => "error.youre_signed_out",
        403 => "error.not_allowed",
        409 => "error.changed",
        429 => "error.busy",
        >= 500 and <= 599 => "error.down",
        _ => "error.ours",
    });

    /// <summary>
    /// Answers a step, as the iPad's <c>answerYourStep</c>: the hand-off read from KIFF first, for the version it signs; one
    /// KIFF no longer waits on isn't sent. The person confirms, then the device key signs <c>kiff.handoff.v1</c>: it grants
    /// nothing, so it's never an approval. A 409 means it ended meanwhile. Each step stops once another session began.
    /// </summary>
    /// <param name="read">A KIFF read as the session: status (0 not reached) and body.</param>
    /// <param name="confirm">The person's confirmation, with the prompt.</param>
    /// <param name="sign">The device key's DER signature over the message, base64.</param>
    /// <param name="post">A KIFF post as the session: status (0 not reached) and body.</param>
    /// <param name="current">Whether the session the answer began in is still the one signed in.</param>
    public static async Task<(StepAnswered Outcome, string? Why)> AnswerAsync(WordBook w, YourStep step, bool done, string tenant,
        Func<string, Task<(int Status, string Body)>> read, Func<string, Task<StepConfirmed>> confirm, Func<byte[], Task<string>> sign,
        Func<string, byte[], Task<(int Status, string Body)>> post, Func<string> signedAt, Func<bool> current)
    {
        var answer = done ? "done" : "skipped";
        var (status, body) = await read(HandoffPath(step.Handoff));
        if (!current()) return (StepAnswered.Stopped, null);
        if (status is < 200 or > 299) return NotAnswered(w, RefusedWords(w, status, body));
        Handoff? handoff = null;
        try { handoff = ReadHandoff(JsonDocument.Parse(body).RootElement); }
        catch (JsonException) { }
        if (handoff is null) return NotAnswered(w, Other(w, w.Get("error.ours")));
        if (!handoff.Waiting) return (StepAnswered.Ended, w.Get("needs_you.your_step.ended"));

        var confirmed = await confirm(Prompt(step, done));
        if (!current()) return (StepAnswered.Stopped, null);
        if (confirmed != StepConfirmed.Yes)
            return (StepAnswered.NotAnswered, w.Get(confirmed == StepConfirmed.Cancelled
                ? "approval.cancelled_nothing_approved_request_kept" : "needs_you.your_step.not_answered_unconfirmed"));
        // signed_at once Windows Hello answered: a slow confirmation never ages the answer KIFF checks for skew.
        var at = signedAt();
        var message = Message(tenant, handoff.Id, answer, handoff.Version, at);
        string value;
        try { value = await sign(message); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            return current() ? (StepAnswered.NotAnswered, w.Get("needs_you.your_step.not_answered_unconfirmed")) : (StepAnswered.Stopped, null);
        }
        if (!current()) return (StepAnswered.Stopped, null);

        (status, body) = await post(AnswerPath(handoff.Id), AnswerBody(answer, at, value));
        if (!current()) return (StepAnswered.Stopped, null);
        if (status == 409) return (StepAnswered.Ended, w.Get("needs_you.your_step.ended"));
        if (status is < 200 or > 299) return NotAnswered(w, RefusedWords(w, status, body));
        return (StepAnswered.Answered, null);
    }

    static (StepAnswered, string?) NotAnswered(WordBook w, string why) => (StepAnswered.NotAnswered, w.Get("needs_you.your_step.not_answered", ("why", why)));

    static JsonElement? Field(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
