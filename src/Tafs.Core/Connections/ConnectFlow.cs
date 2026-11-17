using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Connections;

/// <summary>
/// The calls connecting an app makes, given by the app: planque's grants read as this device; KIFF's approval window read, and
/// opened by this PC's passkey (one Windows Hello; closed, it throws <see cref="OperationCanceledException"/>); a tap inside it
/// to planque (a path and body, signed <c>v2</c> with the window's id); the vendor's sign-in opened in the browser, waiting for
/// planque's <c>tafs:</c> return with its one-use code (none when it didn't come); and whether the session is still the one.
/// </summary>
public sealed record ConnectCalls(Func<Task<IReadOnlyList<PlanqueGrant>?>> Grants, Func<Task<OpenWindow?>> ReadWindow,
    Func<Task<(OpenWindow? Window, string? Why)>> OpenWindow, Func<string, string, byte[], Task<(int Status, string Body)>> Tap,
    Func<Uri, string, Task<string?>> SignIn, Func<bool> Current, Func<Exception, string> Why);

/// <summary>
/// What approving a connection came to: connected (or planque holding the sign-in, <c>listed</c>), the grant that changed since
/// it was shown (to read again, nothing approved), or why not; <paramref name="NotApprovable"/> for a grant in a state no
/// approval moves. All empty: Windows Hello closed, or the sign-in left, nothing said.
/// </summary>
public sealed record ConnectResult(bool Connected, PlanqueGrant? Changed = null, string? Why = null, bool NotApprovable = false);

/// <summary>
/// Approving a connection on this PC (P9), as the iPad's <c>connect(grantID:shown:)</c> for a grant not yet connected: the grant
/// read as it is now and approved only as shown (its digest, and KIFF's words for exactly its profile); one Windows Hello opens
/// KIFF's approval window with this PC's passkey; inside it, planque's <c>approve</c> as a tap; then the vendor's own sign-in
/// in the browser, and planque's <c>confirm</c> with the code it brought back, as a tap again. Connected only once planque says
/// so. A window that ended meanwhile is opened once more.
/// </summary>
public static class ConnectFlow
{
    /// <summary>The states an approval moves: a grant planque failed or disconnected isn't approved again.</summary>
    public static bool Approvable(PlanqueGrant g) => g.Status is "pending" or "approved" or "signed_in" or "needs_reconnect";

    public static async Task<ConnectResult> ApproveAsync(WordBook w, string tenant, string grantId, string? shownDigest, ProfileWords? shownWords,
        ConnectCalls calls)
    {
        var none = new ConnectResult(false);
        var grants = await calls.Grants();
        if (!calls.Current()) return none;
        if (grants is null) return new(false, Why: w.Get("connect.approve_not_read", ("why", w.Get("kiff.kiff_did_not_answer_expected"))));
        if (grants.FirstOrDefault(g => g.Id == grantId) is not { } grant) return new(false, Why: w.Get("connect.no_such_connection"));
        string Vendor() => grant.VendorName ?? grant.Request.Server;
        if (!Approvable(grant))
        {
            var status = grant.Status.Replace('_', ' ');
            return new(false, Why: grant.Reason is { } reason ? w.Get("connect.connection_is_because", ("status", status), ("reason", reason))
                : w.Get("connect.connection_is", ("status", status)), NotApprovable: true);
        }
        if (!grant.DigestMatches) return new(false, Why: w.Get("error.request_mismatch"));
        // What the person read is what's approved: changed since, it's shown again.
        if (grant.Digest != shownDigest) return new(false, Changed: grant);
        // A connection with a profile is approved only with KIFF's words for exactly it shown.
        if (grant.Profile is { } profile && shownWords?.For(profile) != true) return new(false, Why: w.Get("connect.profile_words_unread"));

        OpenWindow? window;
        try { window = await WindowAsync(calls, fresh: false); }
        catch (OperationCanceledException) { return none; }
        catch (WindowRefused refused) { return calls.Current() ? new(false, Why: refused.Message) : none; }
        if (!calls.Current() || window is null) return none;

        var body = ApprovalWindow.ApproveBody(grant.Digest, shownWords?.ProfileSha256, shownWords?.WordsSha256);
        (int Status, string Body, OpenWindow? Window) approved;
        try { approved = await TapAsync(calls, window, ApprovalWindow.ApprovePath(tenant, grant.Id), body); }
        catch (OperationCanceledException) { return none; }
        catch (WindowRefused refused) { return calls.Current() ? new(false, Why: refused.Message) : none; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return calls.Current() ? Failed(w, Vendor(), w.Get("error.offline")) : none; }
        if (!calls.Current()) return none;
        if (approved.Status is < 200 or > 299) return Failed(w, Vendor(), Refusal(w, approved.Status, approved.Body));
        if (Read(approved.Body) is not { } answer) return Failed(w, Vendor(), w.Get("kiff.kiff_did_not_answer_expected"));

        // The vendor's own page, in the browser; planque sends it back to tafs with a one-use code. Or planque holds a fresh
        // sign-in already and gave the code itself.
        string? code = answer.SignIn is { } page ? await calls.SignIn(page, grant.Id) : answer.Status == "signed_in" ? answer.ConfirmCode : null;
        if (!calls.Current()) return none;
        if (code is null) return answer.SignIn is not null ? none : new(false, Why: w.Get("coach.not_connected", ("vendor", Vendor())));

        (int Status, string Body, OpenWindow? Window) confirmed;
        try { confirmed = await TapAsync(calls, approved.Window ?? window, ApprovalWindow.ConfirmPath(tenant, grant.Id), ApprovalWindow.ConfirmBody(code)); }
        catch (OperationCanceledException) { return none; }
        catch (WindowRefused refused) { return calls.Current() ? new(false, Why: refused.Message) : none; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return calls.Current() ? Failed(w, Vendor(), w.Get("error.offline")) : none; }
        if (!calls.Current()) return none;
        if (confirmed.Status is < 200 or > 299) return Failed(w, Vendor(), Refusal(w, confirmed.Status, confirmed.Body));

        // Connected only once planque says so.
        var after = await calls.Grants();
        if (!calls.Current()) return none;
        return after?.FirstOrDefault(g => g.Id == grant.Id) is { Connected: true } or { Listed: true }
            ? new(true) : new(false, Why: w.Get("coach.not_connected", ("vendor", Vendor())));
    }

    /// <summary>
    /// The connect step tafs cloud asked for on Coach's approval card, as the iPad's <c>makeAskedCall</c> and <c>connectedAfter</c>:
    /// planque's approve with the body tafs cloud built, sent as it is, as a tap in KIFF's window (opened with Windows Hello when
    /// none is); then the vendor's sign-in and planque's confirm, or the code planque gave. What came of it, as the report says it.
    /// Windows Hello closed throws <see cref="OperationCanceledException"/>: nothing approved.
    /// </summary>
    /// <param name="body">The approve body this PC rebuilt from what the card showed (<see cref="Tafs.Core.Coach.CoachApproval.ApproveBodyShown"/>).</param>
    public static async Task<Tafs.Core.Coach.ConnectCame> AskedAsync(WordBook w, string tenant, Tafs.Core.Coach.ApprovalPlanque p, byte[] body, ConnectCalls calls)
    {
        OpenWindow? window;
        try { window = await WindowAsync(calls, fresh: false); }
        catch (WindowRefused refused) { return new("failed", Why: refused.Message); }
        if (window is null) throw new OperationCanceledException();
        try
        {
            var approved = await TapAsync(calls, window, ApprovalWindow.ApprovePath(tenant, p.Grant), body);
            if (approved.Status is < 200 or > 299) return new("failed", Why: Refusal(w, approved.Status, approved.Body));
            if (Read(approved.Body) is not { } answer) return new("failed", Why: w.Get("kiff.kiff_did_not_answer_expected"));
            string? code;
            if (answer.SignIn is { } page)
            {
                code = await calls.SignIn(page, p.Grant);
                if (code is null) return new("sign_in_closed");
            }
            else if (answer.Status == "signed_in" && answer.ConfirmCode is { } given) code = given;
            // planque already holds the sign-in: approving connected it, with no second sign-in.
            else return answer.Status is "connected" or "listed" ? new(answer.Status) : new("not_connected", answer.Status);
            var confirmed = await TapAsync(calls, approved.Window ?? window, ApprovalWindow.ConfirmPath(tenant, p.Grant), ApprovalWindow.ConfirmBody(code));
            if (confirmed.Status is < 200 or > 299) return new("failed", Why: Refusal(w, confirmed.Status, confirmed.Body));
            var status = Status(confirmed.Body);
            return status is "connected" or "listed" ? new(answer.SignIn is null && status == "listed" ? "listed" : "connected")
                : answer.SignIn is null ? new("not_connected", status)
                : new("failed", Why: w.Get("connect.sign_came_back_connection", ("status", status ?? "")));
        }
        catch (WindowRefused refused) { return new("failed", Why: refused.Message); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return new("failed", Why: w.Get("error.offline")); }
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

    static ConnectResult Failed(WordBook w, string vendor, string why) => new(false, Why: w.Get("connect.could_not_connected", ("vendor", vendor), ("why", why)));

    /// <summary>KIFF or planque refused opening a window: its words, said in place of a connection.</summary>
    sealed class WindowRefused(string why) : Exception(why);

    /// <summary>The window open now, else one opened with Windows Hello; <paramref name="fresh"/> opens a new one whatever is read.</summary>
    static async Task<OpenWindow?> WindowAsync(ConnectCalls calls, bool fresh)
    {
        if (!fresh && await calls.ReadWindow() is { } open && ApprovalWindow.Usable(open, DateTimeOffset.UtcNow)) return open;
        if (!calls.Current()) return null;
        var (window, why) = await calls.OpenWindow();
        if (window is null && why is not null) throw new WindowRefused(why);
        return window;
    }

    /// <summary>A tap inside the window; one that ended meanwhile (<c>window_required</c>) is opened again with Windows Hello, once.</summary>
    static async Task<(int Status, string Body, OpenWindow? Window)> TapAsync(ConnectCalls calls, OpenWindow window, string path, byte[] body)
    {
        var (status, answer) = await calls.Tap(window.Id, path, body);
        if (status != 403 || Code(answer) != ApprovalWindow.Ended || !calls.Current()) return (status, answer, null);
        var again = await WindowAsync(calls, fresh: true);
        if (again is null) throw new OperationCanceledException();
        var (status2, answer2) = await calls.Tap(again.Id, path, body);
        return (status2, answer2, again);
    }

    static PlanqueApproved? Read(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ApprovalWindow.Approved(doc.RootElement);
        }
        catch (JsonException) { return null; }
    }

    static string? Code(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Lenient.Text(doc.RootElement, "code") ?? Lenient.Text(doc.RootElement, "error") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>planque's refusal: its words or error, else "planque answered 409".</summary>
    static string Refusal(WordBook w, int status, string body) => CloudRead.Refused(w, status, body).Why ?? "";
}
