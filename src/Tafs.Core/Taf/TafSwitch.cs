using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A switch to version <paramref name="N"/> under way: nothing of the taf runs until it ends; its Card draft, if it brings one.</summary>
public sealed record VersionSwitch(int N, string? Draft);

/// <summary>
/// What beginning a switch came to: the switch, or why not; <paramref name="Blocked"/> when a run of the taf is in the way, which
/// "Stop it and install" clears.
/// </summary>
public sealed record SwitchBegun(VersionSwitch? Switch, string? Why, bool Blocked = false);

/// <summary>
/// What Install came to: live, or the Card that changed since it was shown (to read again), or a Card KIFF says moved on since it
/// was drafted (<paramref name="Stale"/>: "Draft its Card again"), or why not. All empty: Windows Hello closed, nothing said.
/// </summary>
public sealed record SwitchResult(bool Live, AgentDraft? Changed = null, bool Stale = false, string? Why = null);

/// <summary>
/// The calls a version switch makes: a trial's (tafs cloud, KIFF's draft, the passkey's confirm) and Windows Hello for the
/// owner's presence, when a version changes no Card and nothing is signed.
/// </summary>
public sealed record SwitchCalls(TrialCalls Calls, Func<string, Task<StepConfirmed>> Presence);

/// <summary>
/// Install this version, forward or back (the iPad's <c>prepareSwitch</c> and <c>useVersion</c>): tafs
/// cloud begins the switch and prepares the Card draft it waits for; the owner reads that Card; one Windows Hello confirms it
/// in KIFF (this PC's passkey) or, with no Card to change, is the owner's presence; then the version goes live. Make it live
/// on the dial's change is this same switch to its next version.
/// </summary>
public static class TafSwitch
{
    static string Version(string taf, int n) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/versions/" + n.ToString(CultureInfo.InvariantCulture);

    /// <summary><c>POST</c>: begins the switch to version n, with <see cref="Body"/>.</summary>
    public static string Path(string taf, int n) => Version(taf, n) + "/switch";

    /// <summary><c>DELETE</c>: drops the switch under way while its Card isn't confirmed; once it is, tafs cloud ends it.</summary>
    public static string SwitchingPath(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/switching";

    /// <summary><c>POST</c>: version n live; refused while its Card isn't confirmed.</summary>
    public static string LivePath(string taf, int n) => Version(taf, n) + "/live";

    /// <summary><c>{"redraft": false, "stop_blocking": false}</c>: its Cards drafted again; what's in the way stopped first.</summary>
    public static byte[] Body(bool redraft, bool stopBlocking) =>
        Encoding.UTF8.GetBytes(new JsonObject { ["redraft"] = redraft, ["stop_blocking"] = stopBlocking }.ToJsonString());

    /// <summary>tafs cloud's <c>{"switching": {"n", "draft", "at"}}</c>; none it can't read.</summary>
    public static VersionSwitch? Read(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Field(a, "switching") is { ValueKind: JsonValueKind.Object } s && Lenient.Int(s, "n") is { } n
            ? new VersionSwitch(n, Lenient.Text(s, "draft") is { Length: > 0 } d ? d : null) : null;

    static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Begins the switch, as the iPad's <c>prepareSwitch</c>: from here nothing of the taf runs. Refused while a run of it is
    /// going or waiting (409): tafs cloud's words, and Stop it and install.
    /// </summary>
    public static async Task<SwitchBegun> BeginAsync(WordBook w, string taf, int n, bool redraft, bool stopBlocking, TrialCalls calls)
    {
        var begun = await calls.Cloud(HttpMethod.Post, Path(taf, n), Body(redraft, stopBlocking));
        if (!calls.Current()) return new(null, null);
        if (begun.Answer is { } a)
            return Read(a) is { } sw ? new(sw, null) : new(null, w.Get("version.cant_go_live", ("n", N(n)), ("why", w.Get("kiff.kiff_did_not_answer_expected"))));
        if (begun.Status == 409 && begun.Words is { Length: > 0 } words) return new(null, words, Blocked: true);
        return new(null, w.Get("version.cant_go_live", ("n", N(n)), ("why", begun.Why ?? w.Get("error.offline"))), Blocked: begun.Status == 409);
    }

    /// <summary>The Card the switch waits for, as KIFF keeps it, read before Windows Hello; or why not.</summary>
    public static async Task<(AgentDraft? Draft, string? Why)> CardAsync(WordBook w, string draft, TrialCalls calls)
    {
        var (read, why) = KiffDrafts.Read(w, await calls.KiffGet(KiffDrafts.Path(draft)));
        return read is not null ? (read, null) : (null, w.Get("version.card_not_read", ("why", why ?? "")));
    }

    /// <summary>Not now, as the iPad's <c>cancelSwitch</c>: the switch dropped, the taf runs as it was. None, else why.</summary>
    public static async Task<string?> CancelAsync(WordBook w, string taf, TrialCalls calls)
    {
        var dropped = await calls.Cloud(HttpMethod.Delete, SwitchingPath(taf), null);
        return dropped.Answer is not null ? null : w.Get("version.switch_not_cancelled", ("why", dropped.Why ?? w.Get("error.offline")));
    }

    /// <summary>
    /// Install, as the iPad's <c>useVersion</c>: the switch begun again (the same one when it's under way), then:
    /// <list type="bullet">
    /// <item>With a Card: the draft read is the one shown (<paramref name="shown"/>, the same id and version), else it's shown
    /// again and the switch dropped; one Windows Hello, this PC's passkey, confirms it in KIFF unless KIFF has it already; KIFF saying a Card moved on
    /// since it was drafted is "Draft its Card again".</item>
    /// <item>Without: Windows Hello for the owner's presence, nothing signed.</item>
    /// </list>
    /// Then the version goes live. Windows Hello closed drops the switch, and the taf runs as it was.
    /// </summary>
    public static async Task<SwitchResult> InstallAsync(WordBook w, string taf, string name, int n, AgentDraft? shown, SwitchCalls switching)
    {
        var calls = switching.Calls;
        var none = new SwitchResult(false);
        var begun = await BeginAsync(w, taf, n, redraft: false, stopBlocking: false, calls);
        if (begun.Switch is not { } sw) return begun.Why is null ? none : new(false, Why: begun.Why);
        async Task<SwitchResult> Dropped(SwitchResult result)
        {
            if (calls.Current()) await CancelAsync(w, taf, calls);
            return result;
        }

        if (sw.Draft is { } id)
        {
            // Nothing shown, nothing signed.
            if (shown is null) return none;
            var (draft, why) = await CardAsync(w, id, calls);
            if (!calls.Current()) return none;
            if (draft is null) return new(false, Why: why);
            // A claimed draft is later than confirmed, its Cards issued all the same.
            var issued = draft.Signed;
            // What the owner read is what's signed: the same draft at the same version.
            if ((draft.Id != shown.Id || draft.Version != shown.Version) && !issued)
                return await Dropped(new(false, Changed: draft, Why: w.Get("version.out_of_date_card")));
            if (!issued)
            {
                var (path, body) = TafTrials.Confirm(draft.Id, draft.Version, asTrial: false);
                (string Header, string Value) signature;
                try { signature = await calls.SignConfirm(path, body); }
                catch (OperationCanceledException) { return await Dropped(none); }
                catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or System.Runtime.InteropServices.COMException
                    or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
                {
                    return calls.Current() ? await Dropped(new(false, Why: w.Get("version.install_unconfirmed", ("n", N(n)),
                        ("why", e is PasskeyRefusedException ? e.Message : calls.Why(e))))) : none;
                }
                if (!calls.Current()) return none;
                int status;
                string answer;
                try { (status, answer) = await calls.KiffPost(path, body, signature); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    return calls.Current() ? new(false, Why: w.Get("version.install_unconfirmed", ("n", N(n)), ("why", w.Get("error.offline")))) : none;
                }
                if (!calls.Current()) return none;
                if (status == 409 && Code(answer) == "card_revised") return new(false, Stale: true, Why: w.Get("version.card_out_of_date", ("n", N(n))));
                if (status is < 200 or > 299)
                    return new(false, Why: w.Get("version.install_unconfirmed", ("n", N(n)), ("why", CoachStart.KiffRefused(w, status, answer))));
            }
        }
        else
        {
            // No Card changes: Windows Hello is the owner's presence, never a record.
            var here = await switching.Presence(w.Get("version.prompt.use", ("n", N(n)), ("taf", name)));
            if (!calls.Current()) return none;
            if (here != StepConfirmed.Yes) return await Dropped(none);
        }

        var live = await calls.Cloud(HttpMethod.Post, LivePath(taf, n), null);
        if (!calls.Current()) return none;
        if (live.Answer is not null) return new(true);
        var notLive = live.Why ?? w.Get("error.offline");
        return new(false, Why: sw.Draft is null ? w.Get("version.not_live", ("n", N(n)), ("why", notLive))
            : w.Get("version.install_unfinished", ("n", N(n)), ("why", notLive)));
    }

    static string? Code(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Lenient.Text(doc.RootElement, "code") : null;
        }
        catch (JsonException) { return null; }
    }
}
