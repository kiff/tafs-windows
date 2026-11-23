using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Connections;

/// <summary>An app's connections for one taf, under its name: the agent that asked for them, and planque's grants in its order.</summary>
public sealed record ConnectionGroup(string Agent, string Taf, IReadOnlyList<PlanqueGrant> Grants);

/// <summary>
/// An app's connections in Connect apps, as the iPad's <c>AppConnectionsView</c> and its model (<c>connections(of:)</c>,
/// <c>opensConnections</c>, <c>connectionGroups</c>, <c>clearPlan</c>, <c>disconnect</c>): the account's planque grants
/// under the app, grouped by the taf each was made for, each ended with Remove access (or Cancel while its sign-in waits),
/// and removed tafs' connections cleared together. Every word is the word book's or tafs cloud's.
/// </summary>
public static class AppConnections
{
    /// <summary>
    /// A connection's last failed call, where an owner whose taf fails looks first (the iPad's <c>lastFailure</c>): the newest
    /// Record line KIFF says wasn't done whose action is one of the connection's tools (planque's list, else its request's
    /// tools the taf uses), the taf's own (its agent) before any other's, as "Last failed: {title} · {detail}". None without
    /// such a line, or with no tools to match.
    /// </summary>
    public static string? LastFailure(WordBook w, PlanqueGrant g, IReadOnlyList<RecordEntry> record)
    {
        var actions = (g.Tools ?? g.Request.Tools.Where(t => t.Tafs).Select(t => t.Action).ToList()).ToHashSet();
        if (actions.Count == 0) return null;
        var failed = record.Where(e => e.Kind == EntryKind.NotDone && e.Call?.ActionName is { } action && actions.Contains(action)).ToList();
        var own = failed.Where(e => e.Call?.ActorId == g.Request.Agent).ToList();
        return (own.Count > 0 ? own : failed).MaxBy(e => e.At) is { } last
            ? w.Get("connect.last_failed", ("title", last.Title), ("detail", last.Detail))
            : null;
    }

    /// <summary>The app's live connections: by the entry planque names for each, or the grant tafs cloud named for the app.</summary>
    public static IReadOnlyList<PlanqueGrant> Of(ConnectApp app, IReadOnlyList<PlanqueGrant> grants) =>
        grants.Where(g => (g.Entry == app.Entry || (app.Grant is { } id && g.Id == id)) && g.Status is not ("disconnected" or "failed")).ToList();

    /// <summary>
    /// Whether an app's row opens its connections, as Android's and the iPad's <c>opensConnections</c>: a
    /// connected app always does, and so does one this PC holds a connection to that isn't connected (a sign-in kept after its
    /// card was declined, planque's <c>listed</c>), so its Remove access is in reach. <c>connected</c> keeps its meaning.
    /// </summary>
    public static bool Opens(ConnectApp app, IReadOnlyList<PlanqueGrant> grants) => app.Connected || Of(app, grants).Count > 0;

    /// <summary>Approved, and waiting on a sign-in at the vendor that hasn't come back: it's cancelled, not removed.</summary>
    public static bool WaitingSignIn(PlanqueGrant g) => g.Status == "approved" && g.SignedIn != true && !g.Connected;

    /// <summary>A request nobody answered yet, holding no sign-in: cancelled, since nothing was connected.</summary>
    public static bool PendingRequest(PlanqueGrant g) => g.Status == "pending" && !g.HoldsSignIn && !g.Connected;

    public static bool Cancellable(PlanqueGrant g) => WaitingSignIn(g) || PendingRequest(g);

    /// <summary>
    /// The row planque draws, as the iPad's <c>rowFromPlanque</c>: when it sends actions for the connection, the row shows its
    /// name, the tafs that use it and its health, and opens the connection's page, where its actions are.
    /// </summary>
    public static bool RowFromPlanque(PlanqueGrant g) => g.Actions is not null;

    /// <summary>
    /// The actions this app knows, in planque's order, as the iPad's <c>Action.known</c>: Sign in again, by approve or by a key,
    /// and Remove or Remove unfinished, by delete. Any other is left out, never guessed at.
    /// </summary>
    public static IReadOnlyList<GrantAction> KnownActions(PlanqueGrant g) => (g.Actions ?? []).Where(a =>
        (a.Id == "sign_in_again" && a.How is "approve" or "key") || (a.Id is "remove" or "remove_unfinished" && a.How == "delete")).ToList();

    /// <summary>An action that ends the connection (<c>delete</c>): asked first, in planque's words, naming what stops.</summary>
    public static bool Removes(GrantAction a) => a.How == "delete";

    /// <summary>The row's word for ending it: "Cancel the request", Cancel for a sign-in that waits, else Remove access.</summary>
    public static string EndWord(WordBook w, PlanqueGrant g) =>
        PendingRequest(g) ? w.Get("connect.cancel_request.button") : WaitingSignIn(g) ? w.Get("common.cancel") : w.Get("connect.disconnect_button");

    /// <summary>What's cancelled, asked first: the request nobody answered, or the sign-in that never came back.</summary>
    public static string CancelTitle(WordBook w, PlanqueGrant g) =>
        w.Get(PendingRequest(g) ? "connect.cancel_request.title" : "connect.cancel_sign_in.title", ("vendor", g.Vendor));

    public static string CancelWord(WordBook w, PlanqueGrant g) =>
        w.Get(PendingRequest(g) ? "connect.cancel_request.button" : "connect.cancel_sign_in.button");

    /// <summary>
    /// The connections grouped by the taf each was made for: tafs with one needing attention first (waiting on its sign-in,
    /// or to sign in again), then the rest by name; then the removed tafs', only those whose agent tafs cloud names as
    /// removed; then those no taf here can name, which aren't proof of a removed taf.
    /// </summary>
    public static (IReadOnlyList<ConnectionGroup> Tafs, IReadOnlyList<PlanqueGrant> Removed, IReadOnlyList<PlanqueGrant> Others) Groups(
        IReadOnlyList<PlanqueGrant> grants, Func<PlanqueGrant, string?> tafOf, IReadOnlyCollection<string> removedAgents)
    {
        var groups = new List<(string Agent, string Taf, List<PlanqueGrant> Grants)>();
        var removed = new List<PlanqueGrant>();
        var others = new List<PlanqueGrant>();
        foreach (var g in grants)
        {
            if (tafOf(g) is not { } name)
            {
                (removedAgents.Contains(g.Request.Agent) ? removed : others).Add(g);
                continue;
            }
            var i = groups.FindIndex(x => x.Agent == g.Request.Agent);
            if (i >= 0) groups[i].Grants.Add(g);
            else groups.Add((g.Request.Agent, name, [g]));
        }
        var needs = groups.Where(x => x.Grants.Any(g => WaitingSignIn(g) || g.NeedsReconnect)).Select(x => x.Agent).ToHashSet();
        var sorted = groups
            .OrderBy(x => needs.Contains(x.Agent) ? 0 : 1)
            .ThenBy(x => x.Taf, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Agent, StringComparer.Ordinal)
            .Select(x => new ConnectionGroup(x.Agent, x.Taf, x.Grants)).ToList();
        return (sorted, removed, others);
    }

    /// <summary>What tafs cloud says of each removed taf's connection, by grant; one it doesn't list is never cleared.</summary>
    public static IReadOnlyDictionary<string, RemovedGrant> RemovedListed(IReadOnlyList<RemovedTaf> removed)
    {
        var listed = new Dictionary<string, RemovedGrant>();
        foreach (var g in removed.SelectMany(t => t.Grants ?? [])) listed.TryAdd(g.Grant, g);
        return listed;
    }

    /// <summary>
    /// Which of <paramref name="ids"/> Clear ends and which it keeps, as the iPad's <c>clearPlan</c>: only a connection tafs
    /// cloud lists as a removed taf's, and one a live taf's Card still uses (<c>used_by</c>) is kept, for those tafs.
    /// </summary>
    public static (IReadOnlyList<PlanqueGrant> Ending, IReadOnlyList<IReadOnlyList<RemovedUsedBy>> KeptFor) ClearPlan(IReadOnlyList<string> ids,
        IReadOnlyList<PlanqueGrant> grants, IReadOnlyDictionary<string, RemovedGrant> listed)
    {
        var asked = grants.Where(g => ids.Contains(g.Id) && listed.ContainsKey(g.Id)).ToList();
        return (asked.Where(g => listed[g.Id].UsedBy.Count == 0).ToList(),
            asked.Where(g => listed[g.Id].UsedBy.Count > 0).Select(g => listed[g.Id].UsedBy).ToList());
    }

    /// <summary>
    /// The other tafs whose live Card draws on a connection's tools, as the iPad's <c>alsoStops</c> and <c>tafsUsing</c>: a Card
    /// rule or a tool on one of the actions it connects (planque's <c>tools</c>, else the request's tafs' tools), leaving out
    /// the taf it was made for and any revoked one. KIFF's tools are the account's, so another taf can draw on it.
    /// </summary>
    public static IReadOnlyList<string> AlsoStops(PlanqueGrant g, IReadOnlyList<Home.HomeTaf> tafs)
    {
        var actions = (g.Tools ?? g.Request.Tools.Where(t => t.Tafs).Select(t => t.Action).ToList()).ToHashSet();
        if (actions.Count == 0) return [];
        return tafs.Where(t => t.Agent != g.Request.Agent && t.State != "revoked" && !t.Revoked && t.CardDraws.Any(actions.Contains))
            .Select(t => t.Name).ToList();
    }

    /// <summary>"Also on Ana's Card", or "Also on the Cards of Ana and Bo", as the iPad's <c>alsoOnCards</c>; none when no other taf.</summary>
    public static string? AlsoOn(WordBook w, IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => w.Get("connect.apps.also_on_one", ("owner", names[0])),
        _ => w.Get("connect.apps.also_on_many", ("tafs", Lists.And(w.Language, names))),
    };

    /// <summary>The question, naming the one taf it ends and the other tafs whose Card draws on its tools; Windows Hello asks the same.</summary>
    public static string DisconnectTitle(WordBook w, string vendor, string? taf, IReadOnlyList<string> alsoStops)
    {
        var asked = taf is { } t ? w.Get("connect.disconnect.title_from", ("vendor", vendor), ("taf", t)) : w.Get("connect.disconnect.title", ("vendor", vendor));
        return alsoStops.Count == 0 ? asked
            : asked + " " + w.Count("connect.disconnect.also_stops", alsoStops.Count, ("names", Lists.And(w.Language, alsoStops)));
    }

    /// <summary>What removing access ends: for a key, the key is deleted from tafs once no taf uses it; for a sign-in, it's revoked.</summary>
    public static string DisconnectMessage(WordBook w, string vendor, string? taf, IReadOnlyList<string> alsoStops, bool key)
    {
        var others = alsoStops.Count switch
        {
            0 => "",
            1 => " " + w.Get("connect.disconnect.also_card", ("owner", alsoStops[0])),
            _ => " " + w.Get("connect.disconnect.also_cards", ("names", Lists.And(w.Language, alsoStops))),
        };
        var first = taf is { } t ? w.Get("connect.disconnect.tools_stop", ("owner", t), ("vendor", vendor)) : w.Get("connect.disconnect.no_taf_uses", ("vendor", vendor));
        return first + others + (key ? " " + w.Get("connect.disconnect.rest_key", ("vendor", vendor))
            : w.Get("connect.other_connections_stay_connected_no", ("vendor", vendor), ("vendor_2", vendor)) + w.Get("connect.connecting_again_asks_you_approve"));
    }

    /// <summary>How clearing went: "Cleared 2. Kept 1 for Chargebacks, whose Card uses it.", and what couldn't be, to try again.</summary>
    public static string Cleared(WordBook w, int done, int kept, IReadOnlyList<string> keptFor, int left)
    {
        var parts = new List<string>();
        if (done > 0) parts.Add(w.Count("connect.clear_removed.cleared", done));
        if (kept > 0)
        {
            var names = Distinct(keptFor);
            var who = names.Count == 0 ? w.Get("connect.kept.another_taf") : Lists.And(w.Language, names);
            parts.Add(w.Count(names.Count > 1 ? "connect.kept.for_cards" : "connect.kept.for_card", kept, ("who", who)));
        }
        if (left > 0) parts.Add(w.Get("connect.could_not_cleared_try_again", ("left", left.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return string.Join(" ", parts);
    }

    /// <summary>Under a removed taf's connection Clear keeps: the tafs whose Cards still use it; none when tafs cloud names none.</summary>
    public static string? KeptBy(WordBook w, IReadOnlyList<string> names)
    {
        var n = Distinct(names);
        return n.Count switch
        {
            0 => null,
            1 => w.Get("connect.used_by.one", ("name", n[0])),
            _ => w.Get("connect.used_by.many", ("names", Lists.And(w.Language, n))),
        };
    }

    static List<string> Distinct(IEnumerable<string> names) => names.Where(n => n.Trim().Length > 0).Distinct().ToList();

    /// <summary>planque's route that ends a connection: it removes its tools from KIFF and revokes the sign-in at the vendor.</summary>
    public static string DisconnectPath(string tenant, string grant) => $"{PlanqueProof.GrantsPath(tenant)}/{CloudRead.PathPart(grant)}";

    /// <summary>
    /// Ending connections, as the iPad's <c>disconnect</c> and <c>clearRemovedConnections</c>: Windows Hello first, asked in
    /// the words of the question (<paramref name="prompt"/>), then planque's DELETE for each, signed by this device. Closed,
    /// nothing is sent ("Not done: it wasn't confirmed."). How many couldn't be ended, and why the last one wasn't; stopped,
    /// with nothing more sent, when another session began.
    /// </summary>
    public static async Task<(int Left, string? Why, bool Stopped)> EndAsync(WordBook w, IReadOnlyList<string> grants, string prompt,
        Func<string, Task<StepConfirmed>> confirm, Func<string, Task<int>> delete, Func<bool> current)
    {
        if (grants.Count == 0) return (0, null, false);
        var confirmed = await confirm(prompt);
        if (!current()) return (grants.Count, null, true);
        if (confirmed != StepConfirmed.Yes) return (grants.Count, w.Get("common.not_done_unconfirmed"), false);
        var (left, why) = (0, (string?)null);
        foreach (var id in grants)
        {
            int status;
            try { status = await delete(id); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { status = 0; }
            if (!current()) return (left + 1, null, true);
            if (status is >= 200 and <= 299) continue;
            left++;
            why = w.Get("connect.not_disconnected", ("why", KiffErrors.Words(w, status)));
        }
        return (left, why, false);
    }
}
