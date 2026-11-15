using System.Text.Json;
using System.Text.RegularExpressions;
using Tafs.Core.Words;

namespace Tafs.Core.Connections;

/// <summary>
/// One app tafs cloud offers, as its Connect apps list sends it. How is oauth or key (before Method: sign_in or key);
/// State is connected, available, needs_reconnect, needs_setup or signed_in; SetupWords is tafs cloud's line for
/// needs_setup; Whose and UsedBy are tafs cloud's words and names, null when it sends none.
/// </summary>
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
    public IReadOnlyList<string>? UsedBy { get; init; }

    public string DisplayName => Name ?? Title;
    public bool Connected => State == "connected" || ConnectedFlag;
    public bool TakesKey => (Method ?? How) == "key";
    public bool NeedsReconnect => State == "needs_reconnect";
    public bool NeedsSetup => State == "needs_setup";
    /// <summary>Connected, signed in, or to sign in again: the person's own, under Yours.</summary>
    public bool Yours => Connected || NeedsReconnect || State == "signed_in";
}

/// <summary>tafs cloud's Connect apps list: the apps in its order, whether more can be listed, and whether Coach connects others.</summary>
public sealed record ConnectAppsList(IReadOnlyList<ConnectApp> Apps, bool ByKey, string? NextCursor);

/// <summary>What a row says: how it connects, whose it is, where it stands, who uses it, or tafs cloud's setup line.</summary>
public sealed record ConnectRow(string How, string? Whose, string? Status, string? UsedBy, string? Setup);

/// <summary>
/// Connect apps, as on the iPad (its ConnectAppsView): the person's own apps under Yours, then the rest of the catalog
/// under Discover, in a table of App, How it connects, Whose and Status. Every word but the app's own is tafs cloud's
/// or the word book's.
/// </summary>
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
                        UsedBy = Names(a),
                    });
        var byKey = root.TryGetProperty("by_key", out var k) && k.ValueKind == JsonValueKind.True;
        return new ConnectAppsList(apps, byKey, Text(root, "next_cursor"));
    }

    /// <summary>A string field, trimmed; null when it's missing, blank or not a string.</summary>
    static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is { Length: > 0 } s ? s : null;

    /// <summary>used_by: [{taf, name}] by tafs cloud's names, or plain names. Anything else reads as none sent.</summary>
    static IReadOnlyList<string>? Names(JsonElement a)
    {
        if (!a.TryGetProperty("used_by", out var u) || u.ValueKind != JsonValueKind.Array) return null;
        return u.EnumerateArray()
            .Select(n => n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : n.ValueKind == JsonValueKind.Object ? Text(n, "name") : null)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
    }

    public static ConnectRow Row(WordBook w, ConnectApp app)
    {
        var status = app.Connected ? w.Get("connect.status.connected") : app.NeedsReconnect ? w.Get("connect.sign_in_again") : null;
        var usedBy = app.Yours && app.UsedBy is { Count: > 0 } names ? w.Get("connect.apps.used_by", ("tafs", Lists.And(w.Language, names))) : null;
        return new ConnectRow(w.Get(app.TakesKey ? "connect.how.key" : "connect.how.sign_in"), app.Whose, status, usedBy,
            app.NeedsSetup ? app.SetupWords ?? "" : null);
    }

    /// <summary>An app tafs cloud hasn't set up for anyone has its name in the soft colour; the others in the ink.</summary>
    public static bool NameIsSoft(ConnectApp app) => app.NeedsSetup;

    /// <summary>The button at a row's end: none to set up, a ✓ when connected, else Connect or Sign in again.</summary>
    public static string? Button(WordBook w, ConnectApp app) =>
        app.NeedsSetup || app.Connected ? null : w.Get(app.NeedsReconnect ? "connect.sign_in_again" : "connect.apps.connect");

    /// <summary>
    /// Yours first, then Discover, each in tafs cloud's order. An app under Yours isn't under Discover again in another of
    /// its catalog forms: Stripe connected as connector:composio:stripe leaves out stripe@3, and the other way round.
    /// </summary>
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
         {"entry":"connector:composio:gmail","vendor":"Gmail","name":"Gmail","subtitle":"Mail: reads, and drafts replies","how":"oauth","method":"sign_in","state":"connected","connected":true,"whose":"Yours","used_by":["Refunds"]},
         {"entry":"stripe@3","vendor":"Stripe","name":"Stripe","subtitle":"Payments: refunds and charges","how":"key","method":"key","state":"connected","connected":true,"whose":"Yours","used_by":["Refunds","Chargebacks"]},
         {"entry":"connector:composio:googlesheets","vendor":"Google Sheets","name":"Google Sheets","subtitle":"Spreadsheets: rows read and written","how":"oauth","method":"sign_in","state":"available"},
         {"entry":"connector:composio:slack","vendor":"Slack","name":"Slack","subtitle":"Messages to your team's channels","how":"oauth","method":"sign_in","state":"available"},
         {"entry":"qonto@1","vendor":"Qonto","name":"Qonto","subtitle":"Invoicing: reads, and invoice drafts from quotes","how":"key","method":"key","state":"available"},
         {"entry":"connector:composio:hubspot","vendor":"HubSpot","name":"HubSpot","subtitle":"Contacts and deals","how":"oauth","method":"sign_in","state":"available"}
        ],"by_key":true}
        """);
}
