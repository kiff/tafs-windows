using System.Text.Json;
using System.Text.RegularExpressions;
using Tafs.Core.Words;

namespace Tafs.Core.Connections;

/// <summary>
/// One app tafs cloud offers, as its Connect apps list sends it. How is oauth or key (before Method: sign_in or key);
/// State is connected, available, needs_reconnect, needs_setup or signed_in; SetupWords is tafs cloud's line for
/// needs_setup; Whose and UsedBy are tafs cloud's words and names, null when it sends none.
/// </summary>
/// <summary>What Connect does: nothing (no taf to connect for), ask which taf, or Ask Coach (no taf needs the app).</summary>
public enum ConnectRoute { NoTaf, Ask, AskCoach }

public sealed record ConnectApp(string Entry)
{
    public string Vendor { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Name { get; init; }
    public string? Subtitle { get; init; }
    public string How { get; init; } = "oauth";
    public string? Method { get; init; }
    public string State { get; init; } = "available";
    public bool ConnectedFlag { get; init; }
    public string? SetupWords { get; init; }
    public string? Whose { get; init; }
    /// <summary>tafs cloud's second line for the row, in the person's language (<c>line</c>), as a narrow row says it.</summary>
    public string? Line { get; init; }
    public IReadOnlyList<string>? UsedBy { get; init; }
    /// <summary>tafs cloud's logo path for it (<c>/v1/logos/…</c>), drawn only through <see cref="ConnectAppsScreen.LogoUrl"/>.</summary>
    public string? Logo { get; init; }
    /// <summary>The grant tafs cloud names for it, when it names one: its connections are planque's under this app too.</summary>
    public string? Grant { get; init; }
    /// <summary>
    /// The tafs that need it, by tafs cloud's id and name (<c>needed_by</c>): "Which taf?" lists only these. None from a tafs
    /// cloud before it: every taf the person manages.
    /// </summary>
    public IReadOnlyList<(string Taf, string Name)>? NeededBy { get; init; }
    /// <summary>
    /// Its live connections made under a taf that doesn't need it (tafs cloud's <c>misplaced</c>, sent only with this PC's
    /// signed grants read): said on the connection, so it can be removed.
    /// </summary>
    public IReadOnlyList<MisplacedGrant> Misplaced { get; init; } = [];

    public string DisplayName => Name ?? Title;
    public bool Connected => State == "connected" || ConnectedFlag;
    public bool TakesKey => (Method ?? How) == "key";
    public bool NeedsReconnect => State == "needs_reconnect";
    public bool NeedsSetup => State == "needs_setup";
    /// <summary>Connected, signed in, or to sign in again: the person's own, under Yours.</summary>
    public bool Yours => Connected || NeedsReconnect || State == "signed_in";
}

/// <summary>A live connection of an app made under a taf that doesn't need it: the grant, and that taf by id and name.</summary>
public sealed record MisplacedGrant(string Grant, string Taf, string Name);

/// <summary>
/// tafs cloud's Connect apps list: the apps in its order, whether Coach connects others, and for More apps the cursor of the
/// next page and the catalog it's from (<paramref name="Provider"/>).
/// </summary>
public sealed record ConnectAppsList(IReadOnlyList<ConnectApp> Apps, bool ByKey, string? NextCursor, string? Provider = null)
{
    /// <summary>
    /// The tafs removed since, as tafs cloud names them (<c>removed_tafs</c>), for someone who manages Cards: a grant whose
    /// agent is one is a removed taf's. Empty from a tafs cloud without it.
    /// </summary>
    public IReadOnlyList<RemovedTaf> RemovedTafs { get; init; } = [];
}

/// <summary>A live taf whose Card still uses a removed taf's connection, by tafs cloud's name.</summary>
public sealed record RemovedUsedBy(string Taf, string Name);

/// <summary>A removed taf's live connection, with the live tafs whose Cards still use it: Clear keeps those.</summary>
public sealed record RemovedGrant(string Grant, IReadOnlyList<RemovedUsedBy> UsedBy);

/// <summary>A taf removed since: its agent, and its live connections, none when tafs cloud couldn't list them (then no Clear).</summary>
public sealed record RemovedTaf(string Agent, IReadOnlyList<RemovedGrant>? Grants);

/// <summary>What a row says: how it connects, whose it is, where it stands, who uses it, or tafs cloud's setup line.</summary>
public sealed record ConnectRow(string How, string? Whose, string? Status, string? UsedBy, string? Setup, string? Line = null)
{
    /// <summary>
    /// A narrow row's second line, as the iPad's <c>ConnectRow.second</c> on a phone: an app to set up says tafs cloud's words
    /// for it; else tafs cloud's line, as sent; else how, whose, where it stands and who uses it, joined by " · ".
    /// </summary>
    public string Second => Setup ?? Line ?? string.Join(" · ", new[] { How, Whose, Status, UsedBy }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>Connect apps, as on the iPad (its ConnectAppsView): Yours, then Discover, in a table. Every word is tafs cloud's or the word book's.</summary>
public static class ConnectAppsScreen
{
    /// <summary>Reads the list leniently, as the iPad does: an app with no entry is left out, never the list.</summary>
    public static ConnectAppsList Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var apps = new List<ConnectApp>();
        if (root.TryGetProperty("apps", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var a in list.EnumerateArray())
                if (a.ValueKind == JsonValueKind.Object && Text(a, "entry") is { } entry)
                    apps.Add(new ConnectApp(entry)
                    {
                        Vendor = Text(a, "vendor") ?? "",
                        Title = Text(a, "title") ?? "",
                        Name = Text(a, "name"),
                        Subtitle = Text(a, "subtitle"),
                        How = Text(a, "how") ?? "oauth",
                        Method = Text(a, "method"),
                        State = Text(a, "state") ?? "available",
                        ConnectedFlag = a.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True,
                        SetupWords = Text(a, "words"),
                        Whose = Text(a, "whose"),
                        Line = Text(a, "line"),
                        UsedBy = Names(a),
                        Logo = Text(a, "logo"),
                        Grant = Text(a, "grant"),
                        NeededBy = a.TryGetProperty("needed_by", out var n) && n.ValueKind == JsonValueKind.Array
                            ? n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Text(x, "taf") is not null)
                                .Select(x => (Text(x, "taf")!, Text(x, "name") ?? "")).ToList() : null,
                        // Read leniently, as the iPad's: an entry without its grant is left out, never the app.
                        Misplaced = a.TryGetProperty("misplaced", out var m) && m.ValueKind == JsonValueKind.Array
                            ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Text(x, "grant") is not null)
                                .Select(x => new MisplacedGrant(Text(x, "grant")!, Text(x, "taf") ?? "", Text(x, "name") ?? "")).ToList() : [],
                    });
        var byKey = root.TryGetProperty("by_key", out var k) && k.ValueKind == JsonValueKind.True;
        return new ConnectAppsList(apps, byKey, Text(root, "next_cursor"), Text(root, "provider")) { RemovedTafs = Removed(root) };
    }

    /// <summary>tafs cloud's <c>removed_tafs</c>, read leniently: one without its agent is left out, never the list.</summary>
    static IReadOnlyList<RemovedTaf> Removed(JsonElement root)
    {
        if (!root.TryGetProperty("removed_tafs", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var removed = new List<RemovedTaf>();
        foreach (var t in list.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object || Text(t, "agent") is not { } agent) continue;
            List<RemovedGrant>? grants = null;
            if (t.TryGetProperty("grants", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                grants = [];
                foreach (var x in g.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.Object || Text(x, "grant") is not { } grant) continue;
                    var used = new List<RemovedUsedBy>();
                    if (x.TryGetProperty("used_by", out var u) && u.ValueKind == JsonValueKind.Array)
                        foreach (var y in u.EnumerateArray())
                            if (y.ValueKind == JsonValueKind.Object) used.Add(new(Text(y, "taf") ?? "", Text(y, "name") ?? ""));
                    grants.Add(new(grant, used));
                }
            }
            removed.Add(new(agent, grants));
        }
        return removed;
    }

    public const string ListPath = "/v1/connect/apps";

    /// <summary>
    /// The read of a page, as the iPad's <c>connectApps</c>: <c>GET /v1/connect/apps</c> with what was typed (trimmed of spaces
    /// and tabs, none when blank), and for a next page its catalog and cursor. Each value is escaped whole, so a cursor's
    /// + or / is never a separator.
    /// </summary>
    public static string Path(string? q = null, string? provider = null, string? cursor = null)
    {
        var items = new List<string>();
        if (q?.Trim(' ', '\t') is { Length: > 0 } words) items.Add("q=" + Taf.CloudRead.QueryPart(words));
        if (provider is not null) items.Add("provider=" + Taf.CloudRead.QueryPart(provider));
        if (cursor is not null) items.Add("cursor=" + Taf.CloudRead.QueryPart(cursor));
        return items.Count == 0 ? ListPath : ListPath + "?" + string.Join('&', items);
    }

    /// <summary>
    /// More apps, as the iPad's <c>loadMoreConnectApps</c>: the next page added under those shown, an app listed already not
    /// again; Coach connects others if either page says so; the next page's cursor and catalog go on.
    /// </summary>
    public static ConnectAppsList More(ConnectAppsList shown, ConnectAppsList page)
    {
        var seen = shown.Apps.Select(a => a.Entry).ToHashSet();
        return page with { Apps = shown.Apps.Concat(page.Apps.Where(a => !seen.Contains(a.Entry))).ToList(), ByKey = shown.ByKey || page.ByKey };
    }

    /// <summary>
    /// A logo's address, as the iPad's <c>logoURL</c>: only tafs cloud's own <c>/v1/logos/</c> with a 32-character lowercase hex
    /// key, on tafs cloud; anything else draws no logo. It's fetched as an image, with no token.
    /// </summary>
    public static Uri? LogoUrl(string? path)
    {
        const string prefix = "/v1/logos/";
        if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var key = path[prefix.Length..];
        return key.Length == 32 && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? new Uri(Kiff.KeptSessions.TafsCloud, path) : null;
    }

    /// <summary>A string field, trimmed; null when it's missing, blank or not a string.</summary>
    static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is { Length: > 0 } s ? s : null;

    /// <summary>used_by: [{taf, name}], by tafs cloud's names, as the iPad reads it. Anything else reads as none sent.</summary>
    static IReadOnlyList<string>? Names(JsonElement a) => a.TryGetProperty("used_by", out var u) && u.ValueKind == JsonValueKind.Array
        ? u.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.Object).Select(n => Text(n, "name")).OfType<string>().ToList()
        : null;

    public static ConnectRow Row(WordBook w, ConnectApp app)
    {
        var status = app.Connected ? w.Get("connect.status.connected") : app.NeedsReconnect ? w.Get("connect.sign_in_again") : null;
        var usedBy = app.Yours && app.UsedBy is { Count: > 0 } names ? w.Get("connect.apps.used_by", ("tafs", Lists.And(w.Language, names))) : null;
        return new ConnectRow(w.Get(app.TakesKey ? "connect.how.key" : "connect.how.sign_in"), app.Whose, status, usedBy,
            app.NeedsSetup ? app.SetupWords ?? "" : null, app.Line);
    }

    /// <summary>
    /// "Apps connect for a taf. Make one first", over Discover only, as the iPad says it: there are apps to connect, the list
    /// wasn't opened from a taf, and no taf can have them. This PC reads no tafs yet, so none can.
    /// </summary>
    public static bool SaysMakeATaf(int discover, bool forTaf = false, int connectableTafs = 0) => discover > 0 && !forTaf && connectableTafs == 0;

    /// <summary>
    /// The tafs an app can be connected for, as the iPad's <c>connectableTafs(for:)</c>: of those on tafs cloud that the person
    /// manages, the ones tafs cloud says need it, in its order; all of them when it doesn't say.
    /// </summary>
    public static IReadOnlyList<(string Id, string Name)> ConnectableTafs(IReadOnlyList<(string Id, string Name)> tafs, ConnectApp app) =>
        app.NeededBy is not { } needed ? tafs : needed.Select(n => tafs.FirstOrDefault(t => t.Id == n.Taf)).Where(t => t.Id is not null).ToList();

    /// <summary>
    /// What Connect does for an app, as the iPad's <c>ConnectRow.connectFor</c>: with tafs cloud's <c>needed_by</c>, ask which of
    /// the tafs that need it, or, when none does, Ask Coach (Coach sets one up, never a guess); without it, ask among every taf
    /// the person manages, or nothing when there's none.
    /// </summary>
    public static ConnectRoute Route(ConnectApp app, IReadOnlyList<(string Id, string Name)> connectable) =>
        connectable.Count > 0 ? ConnectRoute.Ask : app.NeededBy is null ? ConnectRoute.NoTaf : ConnectRoute.AskCoach;

    /// <summary>Starting a connection for a taf: <c>POST /v1/connect/apps/{entry}</c> with <c>{"taf"}</c>, this PC's grants read proof beside it.</summary>
    public static string StartPath(string entry) => "/v1/connect/apps/" + Tafs.Core.Taf.CloudRead.PathPart(entry);

    public static byte[] StartBody(string taf) => System.Text.Encoding.UTF8.GetBytes(new System.Text.Json.Nodes.JsonObject { ["taf"] = taf }.ToJsonString());

    /// <summary>tafs cloud's answer: the grant to approve (its id, or planque's grant with it), and whether it takes a key.</summary>
    public static (string Grant, bool NeedsKey)? Started(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("grant", out var g)) return null;
        var id = g.ValueKind == JsonValueKind.String ? g.GetString() : g.ValueKind == JsonValueKind.Object ? Text(g, "id") : null;
        return string.IsNullOrEmpty(id) ? null : (id, a.TryGetProperty("needs_key", out var k) && k.ValueKind == JsonValueKind.True);
    }

    /// <summary>An app tafs cloud hasn't set up for anyone has its name in the soft colour; the others in the ink.</summary>
    public static bool NameIsSoft(ConnectApp app) => app.NeedsSetup;

    /// <summary>The button at a row's end: none to set up, a ✓ when connected, else Connect or Sign in again.</summary>
    public static string? Button(WordBook w, ConnectApp app) =>
        app.NeedsSetup || app.Connected ? null : w.Get(app.NeedsReconnect ? "connect.sign_in_again" : "connect.apps.connect");

    /// <summary>Yours, then Discover, in tafs cloud's order; an app under Yours isn't under Discover in another catalog form.</summary>
    public static (IReadOnlyList<ConnectApp> Yours, IReadOnlyList<ConnectApp> Discover) Split(IReadOnlyList<ConnectApp> apps)
    {
        var yours = apps.Where(a => a.Yours).ToList();
        var had = yours.SelectMany(Keys).ToHashSet();
        return (yours, apps.Where(a => !a.Yours && !Keys(a).Overlaps(had)).ToList());
    }

    /// <summary>The names an app goes by across the catalog's forms: its entry's last part without its version, and its vendor, folded.</summary>
    public static HashSet<string> Keys(ConnectApp app)
    {
        var last = app.Entry.Split(':')[^1];
        var slug = Regex.Replace(last, @"@\d+$", "");
        return new[] { slug, app.Vendor }.Select(Fold).Where(k => k.Length > 0).ToHashSet();
    }

    static string Fold(string name) => new(name.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c) && !".-_".Contains(c)).ToArray());

    public const double Spacing = 12;
    public const double AppLeast = 200;
    static readonly double[] Wide = [150, 130, 230];
    static readonly double[] Least = [96, 84, 180];

    /// <summary>
    /// Too narrow for the table, as the iPad's compact width: the four columns at their least don't fit, so each row is its
    /// name over one line (<see cref="ConnectRow.Second"/>) and its button, with no column names.
    /// </summary>
    public static bool Narrow(double total) => total - Spacing * 3 < AppLeast + Least.Sum();

    /// <summary>The four widths in <paramref name="total"/>, as the iPad's ConnectColumns: App's room first, then the three give way together.</summary>
    public static double[] Widths(double total)
    {
        var room = Math.Max(0, total - Spacing * 3);
        var wideSum = Wide.Sum();
        if (room - wideSum >= AppLeast) return [room - wideSum, .. Wide];
        var give = wideSum - Least.Sum();
        var need = Math.Min(give, AppLeast - (room - wideSum));
        var others = Wide.Zip(Least, (w, l) => give > 0 ? w - (w - l) * need / give : w).ToArray();
        return [Math.Max(0, room - others.Sum()), .. others];
    }

    /// <summary>The iPad's sample for the reference screenshots: two apps connected, four to connect.</summary>
    public static ConnectAppsList Sample() => Read("""
        {"apps":[
         {"entry":"connector:composio:gmail","vendor":"Gmail","name":"Gmail","subtitle":"Mail: reads, and drafts replies","how":"oauth","method":"sign_in","state":"connected","connected":true,"whose":"Yours"},
         {"entry":"zendesk@1","vendor":"Zendesk","name":"Zendesk","subtitle":"Helpdesk: reads tickets, and replies","how":"key","method":"key","state":"connected","connected":true,"whose":"Yours"},
         {"entry":"connector:composio:googlesheets","vendor":"Google Sheets","name":"Google Sheets","subtitle":"Spreadsheets: rows read and written","how":"oauth","method":"sign_in","state":"available"},
         {"entry":"connector:composio:slack","vendor":"Slack","name":"Slack","subtitle":"Messages to your team's channels","how":"oauth","method":"sign_in","state":"available"},
         {"entry":"pipedrive@1","vendor":"Pipedrive","name":"Pipedrive","subtitle":"Deals and contacts","how":"key","method":"key","state":"available"},
         {"entry":"connector:composio:hubspot","vendor":"HubSpot","name":"HubSpot","subtitle":"Contacts and deals","how":"oauth","method":"sign_in","state":"available"}
        ],"by_key":true}
        """);
}
