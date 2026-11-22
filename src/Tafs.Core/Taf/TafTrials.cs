using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A taf's open trial, as the iPad's <c>TafsCloud.OpenTrial</c>: the owner chats with version <paramref name="N"/> under a trial
/// Card of its own (KIFF's <paramref name="Draft"/>, trying), while the live version keeps its schedule, Card and budget. One a
/// taf. Its guidance is tafs cloud's words: a badge ("Trial"), a title ("Trying version 2"), what the version changes, the task
/// to try it with, and when it ends unused; an older tafs cloud sends none of them.
/// </summary>
public sealed record OpenTrial(string Id, int N, string Draft, string? Session, string? Label = null, string? Title = null,
    string? Changed = null, string? Try = null, DateTimeOffset? Ends = null)
{
    /// <summary>Its chat's name, as the menu names it: "Refunds · v2".</summary>
    public string ChatName(WordBook w, string taf) => w.Get("version.trial.chat_name", ("taf", taf), ("n", N.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Its line under that name: its title (else "Trying version 2"), and "ends 24 Nov if unused".</summary>
    public string Line(WordBook w, TimeZoneInfo zone) =>
        string.Join(" · ", new[]
        {
            Title ?? w.Get("version.trial.trying", ("n", N.ToString(CultureInfo.InvariantCulture))),
            Ends is { } ends ? w.Get("version.trial.ends_if_unused", ("date", Day(w, ends, zone))) : null,
        }.OfType<string>());

    /// <summary>
    /// What the trial is, before its first message, as the iPad's <c>TrialScreen.guide</c>: what's new, the task to try it with
    /// (else "Give it a task that uses the change"), then that the live taf keeps working, when it ends, and Install or Go back.
    /// </summary>
    public IReadOnlyList<string> Guide(WordBook w, string taf, TimeZoneInfo zone) =>
        new[]
        {
            Changed is { } changed ? w.Get("version.guide.whats_new", ("changed", changed)) : null,
            Try is { } tryWith ? w.Get("version.guide.try_with", ("try_with", tryWith)) : w.Get("version.guide.give_task", ("name", taf)),
            string.Join(" ", new[]
            {
                w.Get("version.guide.keeps_working", ("name", taf)),
                Ends is { } ends ? w.Get("version.guide.trial_ends", ("date", Day(w, ends, zone))) : null,
                w.Get("version.guide.install_or_back"),
            }.OfType<string>()),
        }.OfType<string>().ToList();

    static string Day(WordBook w, DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM", Tafs.Core.Words.Locale.Culture(w.Language));
}

/// <summary>
/// What Start trying came to: the trial open, or the Card that changed since it was shown (to read again), or why not.
/// </summary>
public sealed record TrialStart(OpenTrial? Open, AgentDraft? Changed, string? Why);

/// <summary>
/// What Install came to: done, or the trial's Card that isn't the one shown (to read again, nothing signed), or why not. All
/// empty: Windows Hello closed, nothing said.
/// </summary>
public sealed record TrialInstall(bool Done, AgentDraft? Changed = null, string? Why = null);

/// <summary>
/// The calls a trial makes, given by the app: tafs cloud's, KIFF's as the session, the confirm signed by this PC's passkey (one
/// Windows Hello; closed, it throws <see cref="OperationCanceledException"/>), the end of a trial signed by this session's device
/// key (taking authority away, so no Windows Hello), and whether the session it began in is still the one signed in.
/// </summary>
/// <param name="Cloud">tafs cloud, as the session: a method, a path and a body (none for a GET or a DELETE).</param>
/// <param name="KiffGet">KIFF's <c>GET</c> of a path, as the session; its refusal in KIFF's words.</param>
/// <param name="SignConfirm">This PC's passkey over KIFF's confirm: a path and its exact body, to its header.</param>
/// <param name="KiffPost">KIFF's <c>POST</c> of a path and body with that header: its status and body.</param>
/// <param name="KiffEnd">KIFF's <c>DELETE</c> of a path, signed by this session's device key: its status and body.</param>
public sealed record TrialCalls(Func<HttpMethod, string, byte[]?, Task<CloudRead>> Cloud, Func<string, Task<CloudRead>> KiffGet,
    Func<string, byte[], Task<(string Header, string Value)>> SignConfirm, Func<string, byte[], (string Header, string Value), Task<(int Status, string Body)>> KiffPost,
    Func<string, Task<(int Status, string Body)>> KiffEnd, Func<bool> Current, Func<Exception, string> Why);

/// <summary>
/// Trying a version (the iPad's <c>TrialViews</c>): tafs cloud drafts the version's trial Card, the owner reads
/// it, this PC's passkey confirms it in KIFF as a trial, and the trial opens with a chat of its own; Install confirms that Card
/// live and makes the version live; Go back ends the trial's Card in KIFF and tafs cloud deletes its chat.
/// </summary>
public static class TafTrials
{
    static string Taf(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf);

    /// <summary>The draft of version n's trial Card, as the iPad's <c>trialDraft</c>: <c>POST /v1/tafs/{id}/versions/{n}/try</c>.</summary>
    public static string TryPath(string taf, int n) => Taf(taf) + "/versions/" + n.ToString(CultureInfo.InvariantCulture) + "/try";

    /// <summary>The taf's open trial: read (<c>GET</c>), opened (<c>PUT</c>, once KIFF has it trying), ended (<c>DELETE</c>).</summary>
    public static string Path(string taf) => Taf(taf) + "/trial";

    /// <summary>One message to the trial, as the iPad's <c>trialMessage</c>.</summary>
    public static string MessagesPath(string taf) => Path(taf) + "/messages";

    /// <summary>The trial's chat, read as a taf's thread is, as the iPad's <c>trialConversation</c>.</summary>
    public static string ConversationPath(string taf) => Path(taf) + "/conversation";

    /// <summary>Install, as the iPad's <c>installTrial</c>: once KIFF confirmed its Card live.</summary>
    public static string InstallPath(string taf) => Path(taf) + "/install";

    /// <summary>KIFF ending a draft's trial, as the iPad's <c>KIFFClient.endTrial(draft:)</c>.</summary>
    public static string EndPath(string draft) => KiffDrafts.Path(draft) + "/trial";

    /// <summary>Opening the trial: <c>{"n": 2, "draft": "d_02"}</c>.</summary>
    public static byte[] OpenBody(int n, string draft) =>
        Encoding.UTF8.GetBytes(new JsonObject { ["n"] = n, ["draft"] = draft }.ToJsonString());

    /// <summary>
    /// KIFF's confirm of the draft at the version read, as a trial (<c>"as": "trial"</c>, the iPad's <c>confirmDraftAsTrial</c>)
    /// or live (Install, the iPad's <c>confirmDraft</c>).
    /// </summary>
    public static (string Path, byte[] Body) Confirm(string draft, string version, bool asTrial)
    {
        var body = new JsonObject { ["version"] = version };
        if (asTrial) body["as"] = "trial";
        return (KiffDrafts.Path(draft) + "/confirm", Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    /// <summary>tafs cloud's <c>{"trial": …}</c>: the open trial, or none (<c>null</c>, or one it can't read).</summary>
    public static OpenTrial? Read(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Field(a, "trial") is { ValueKind: JsonValueKind.Object } t ? Trial(t) : null;

    /// <summary>One open trial as tafs cloud sends it (its answer's <c>trial</c>, or a Home taf's); none it can't read.</summary>
    public static OpenTrial? Trial(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        if (Lenient.Text(t, "id") is not { } id || Lenient.Int(t, "n") is not { } n || Lenient.Text(t, "draft") is not { Length: > 0 } draft) return null;
        return new(id, n, draft, Lenient.Text(t, "session"), Lenient.Text(t, "label"), Lenient.Text(t, "title"),
            Lenient.Text(t, "changed"), Lenient.Text(t, "try"), Lenient.At(t, "ends"));
    }

    /// <summary>The draft's id from tafs cloud's <c>{"draft": "d_02"}</c>.</summary>
    static string? DraftId(JsonElement a) => a.ValueKind == JsonValueKind.Object ? Lenient.Text(a, "draft") is { Length: > 0 } d ? d : null : null;

    /// <summary>
    /// The taf's open trial, as the iPad's <c>loadTrial</c>: none when it has none (one that ended elsewhere leaves here too),
    /// or why it couldn't be read.
    /// </summary>
    public static async Task<(OpenTrial? Open, string? Why)> LoadAsync(WordBook w, string taf, Func<string, Task<CloudRead>> read)
    {
        var answer = await read(Path(taf));
        if (answer.Answer is { } a) return (Read(a), null);
        return (null, w.Get("version.trial.not_read", ("why", answer.Why ?? w.Get("error.offline"))));
    }

    /// <summary>
    /// The Card a trial of version n runs under, as the owner reads it before Windows Hello (the iPad's <c>trialCard</c>):
    /// the draft tafs cloud prepares for it, as KIFF keeps it; or why not, "Version 2's trial Card couldn't be read."
    /// </summary>
    public static async Task<(AgentDraft? Draft, string? Why)> CardAsync(WordBook w, string taf, int n, TrialCalls calls)
    {
        var (draft, why) = await DraftAsync(w, taf, n, calls);
        return draft is not null ? (draft, null) : (null, why is null ? null : w.Get("version.trial.card_not_read", ("n", N(n)), ("why", why)));
    }

    static async Task<(AgentDraft? Draft, string? Why)> DraftAsync(WordBook w, string taf, int n, TrialCalls calls)
    {
        var tried = await calls.Cloud(HttpMethod.Post, TryPath(taf, n), null);
        if (!calls.Current()) return (null, null);
        if (tried.Answer is not { } a) return (null, tried.Why ?? w.Get("error.offline"));
        if (DraftId(a) is not { } id) return (null, w.Get("kiff.kiff_did_not_answer_expected"));
        var read = await calls.KiffGet(KiffDrafts.Path(id));
        if (!calls.Current()) return (null, null);
        return KiffDrafts.Read(w, read);
    }

    /// <summary>
    /// Start trying version n, as the iPad's <c>startTrying</c>: the very draft the owner read (<paramref name="shown"/>), read
    /// again by its id, never drafted again, is the one signed, at the version shown; changed since, it's shown again to read.
    /// One Windows Hello, this PC's passkey, confirms it in KIFF as a trial; only a KIFF that answers "trying" runs one, and only
    /// then does tafs cloud open it. Windows Hello closed sends nothing and says nothing.
    /// </summary>
    public static async Task<TrialStart> StartAsync(WordBook w, string taf, int n, AgentDraft shown, TrialCalls calls)
    {
        var none = new TrialStart(null, null, null);
        TrialStart Failed(string why) => new(null, null, w.Get("version.trial.not_started", ("n", N(n)), ("why", why)));
        var (draft, why) = KiffDrafts.Read(w, await calls.KiffGet(KiffDrafts.Path(shown.Id)));
        if (!calls.Current()) return none;
        if (draft is null) return new(null, null, w.Get("version.trial.cant_try", ("n", N(n)), ("why", why ?? "")));
        // What the owner read is what's signed: the same draft at the same version, else it's read again.
        if (draft.Id != shown.Id || draft.Version != shown.Version) return new(null, draft, w.Get("version.trial.changed"));

        var (path, body) = Confirm(draft.Id, draft.Version, asTrial: true);
        (string Header, string Value) signature;
        try { signature = await calls.SignConfirm(path, body); }
        catch (OperationCanceledException) { return none; }
        catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or System.Runtime.InteropServices.COMException
            or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return calls.Current() ? Failed(e is PasskeyRefusedException ? e.Message : calls.Why(e)) : none;
        }
        if (!calls.Current()) return none;
        int status;
        string answer;
        try { (status, answer) = await calls.KiffPost(path, body, signature); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return calls.Current() ? Failed(w.Get("error.offline")) : none; }
        if (!calls.Current()) return none;
        if (status is < 200 or > 299) return Failed(CoachStart.KiffRefused(w, status, answer));
        // Only a KIFF that runs trials answers "trying"; anything else is never taken for a trial.
        var confirmed = Status(answer);
        if (confirmed != "trying")
            return new(null, null, confirmed == "confirmed" ? w.Get("version.not_a_trial.confirmed")
                : w.Get("version.not_a_trial.other", ("status", confirmed ?? "")));

        var opened = await calls.Cloud(HttpMethod.Put, Path(taf), OpenBody(n, draft.Id));
        if (!calls.Current()) return none;
        if (opened.Answer is not { } o) return Failed(opened.Why ?? w.Get("error.offline"));
        return Read(o) is { } open ? new(open, null, null) : Failed(w.Get("kiff.kiff_did_not_answer_expected"));
    }

    static string? Status(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Lenient.Text(doc.RootElement, "status") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The trial's Card as KIFF keeps it, shown before Install asks Windows Hello; or why not.</summary>
    public static async Task<(AgentDraft? Draft, string? Why)> TrialCardAsync(WordBook w, OpenTrial open, TrialCalls calls)
    {
        var (draft, why) = KiffDrafts.Read(w, await calls.KiffGet(KiffDrafts.Path(open.Draft)));
        return draft is not null ? (draft, null) : (null, w.Get("version.trial.card_not_read", ("n", N(open.N)), ("why", why ?? "")));
    }

    /// <summary>
    /// Install, as the iPad's <c>installTrial</c>: the trial's Card confirmed live with one Windows Hello, at exactly the version
    /// the Install question showed (<paramref name="shownVersion"/>); a Card that isn't that one is shown again, nothing signed.
    /// KIFF having it confirmed already grants nothing new: no Windows Hello. Then tafs cloud makes the version live and ends the
    /// trial.
    /// </summary>
    public static async Task<TrialInstall> InstallAsync(WordBook w, string taf, OpenTrial open, string? shownVersion, TrialCalls calls)
    {
        var none = new TrialInstall(false);
        TrialInstall Unfinished(string why) => new(false, Why: w.Get("version.trial.install_unfinished", ("n", N(open.N)), ("why", why)));
        var read = await calls.KiffGet(KiffDrafts.Path(open.Draft));
        if (!calls.Current()) return none;
        var (draft, why) = KiffDrafts.Read(w, read);
        if (draft is null) return Unfinished(why ?? "");
        if (!draft.Confirmed)
        {
            // What the owner read is what's signed: another version, it's read again.
            if (draft.Version != shownVersion) return new(false, draft, w.Get("version.out_of_date_card"));
            var (path, body) = Confirm(draft.Id, draft.Version, asTrial: false);
            (string Header, string Value) signature;
            try { signature = await calls.SignConfirm(path, body); }
            catch (OperationCanceledException) { return none; }
            catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or System.Runtime.InteropServices.COMException
                or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
            {
                return calls.Current() ? Unfinished(e is PasskeyRefusedException ? e.Message : calls.Why(e)) : none;
            }
            if (!calls.Current()) return none;
            try
            {
                var (status, answer) = await calls.KiffPost(path, body, signature);
                if (!calls.Current()) return none;
                if (status is < 200 or > 299) return Unfinished(CoachStart.KiffRefused(w, status, answer));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return calls.Current() ? Unfinished(w.Get("error.offline")) : none; }
        }
        var installed = await calls.Cloud(HttpMethod.Post, InstallPath(taf), null);
        if (!calls.Current()) return none;
        return installed.Answer is not null ? new(true) : Unfinished(installed.Why ?? w.Get("error.offline"));
    }

    /// <summary>
    /// Go back, as the iPad's <c>goBackFromTrial</c>: KIFF ends the trial's Card, signed by this session's device key (it takes
    /// authority away, so no Windows Hello; one KIFF had ended already, 404 or 409, counts as ended); then tafs cloud ends the
    /// trial and deletes its chat. None once done, else why ("The trial didn't end.").
    /// </summary>
    public static async Task<string?> GoBackAsync(WordBook w, string taf, OpenTrial open, TrialCalls calls)
    {
        string NotEnded(string why) => w.Get("version.trial.not_ended", ("why", why));
        try
        {
            var (status, answer) = await calls.KiffEnd(EndPath(open.Draft));
            if (!calls.Current()) return "";
            if (status is (< 200 or > 299) and not (404 or 409)) return NotEnded(CoachStart.KiffRefused(w, status, answer));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return calls.Current() ? NotEnded(w.Get("error.offline")) : ""; }
        catch (Exception e) when (e is InvalidDataException or System.Runtime.InteropServices.COMException
            or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return calls.Current() ? NotEnded(calls.Why(e)) : "";
        }
        var ended = await calls.Cloud(HttpMethod.Delete, Path(taf), null);
        if (!calls.Current()) return "";
        return ended.Answer is not null ? null : NotEnded(ended.Why ?? w.Get("error.offline"));
    }

    static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
