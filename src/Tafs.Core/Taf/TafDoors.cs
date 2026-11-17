using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>Someone a door knows: their address or number, the name given, and whether tafs paired them (the owner's own sign-in).</summary>
public sealed record DoorKey(string Id, string? Name, bool Auto);

/// <summary>tafs cloud's words for a door: where it stands, what a tap on it does, and that its settings are below.</summary>
public sealed record DoorWords(string State, string Tap, string Settings);

/// <summary>
/// One door (the iPad's <c>TafsCloud.Door</c>, docs/doors.md): an email address, a WhatsApp number or a Slack channel where
/// work reaches the taf from outside, the answer going back the same way. <c>proven</c>: it can tell who wrote, so it holds keys.
/// </summary>
public sealed record Door(string Id, string App, string Kind, string Label, bool Open, string Address, string Proof, string How,
    IReadOnlyList<DoorKey> Keys, int Today, int Limit, bool Resting, int? OthersToday = null, int? OthersLimit = null, DoorWords? Words = null)
{
    public bool HoldsKeys => Proof == "proven";
}

/// <summary>An app a door can open on here: its id, label and kind, and how it knows who's writing.</summary>
public sealed record DoorApp(string Id, string Label, string Kind, string Proof, string How);

/// <summary>
/// Someone without a key wrote (the iPad's <c>Knock</c>): the person decides, Do it, Pair or Ignore. A line (<see cref="Rest"/>)
/// isn't a message: it says the door had its day's messages, and how many more were dropped.
/// </summary>
public sealed record Knock(string Id, string Door, string App, string From, string? Name, string Proof, string? Subject, string Text,
    int Attachments, DateTimeOffset At, string State, string? Rest, int Dropped, bool Paired)
{
    public bool IsLine => Rest is not null;
    /// <summary>Who wrote, as the person reads it: "Mara (mara@customer.example)".</summary>
    public string Who => Name is { Length: > 0 } n ? $"{n} ({From})" : From;
}

/// <summary>
/// A taf's doors (P13, the iPad's <c>DoorsModel</c> and <c>TafsCloud+Doors</c>): read with the session
/// (<c>GET /v1/tafs/{id}/doors</c>), and changed the same way: one added, opened or closed, removed, someone paired or unpaired,
/// a knock answered. tafs cloud keeps them, and its Card still decides what the taf does.
/// </summary>
public sealed record TafDoors(IReadOnlyList<Door> Doors, IReadOnlyList<DoorApp> Apps, IReadOnlyList<Knock> Knocks)
{
    static string Taf(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/doors";

    public static string Path(string taf) => Taf(taf);
    /// <summary><c>PATCH</c> opens or closes it, <c>DELETE</c> removes it.</summary>
    public static string DoorPath(string taf, string door) => Taf(taf) + "/" + CloudRead.PathPart(door);
    /// <summary><c>POST</c> pairs someone.</summary>
    public static string KeysPath(string taf, string door) => DoorPath(taf, door) + "/keys";
    /// <summary><c>DELETE</c> unpairs them.</summary>
    public static string KeyPath(string taf, string door, string sender) => KeysPath(taf, door) + "/" + CloudRead.PathPart(sender);
    /// <summary><c>POST</c> answers a knock.</summary>
    public static string KnockPath(string taf, string knock) => Taf(taf) + "/knocks/" + CloudRead.PathPart(knock);

    static byte[] Json(JsonObject o) => Encoding.UTF8.GetBytes(o.ToJsonString());
    public static byte[] AddBody(string app) => Json(new JsonObject { ["app"] = app });
    public static byte[] OpenBody(bool open) => Json(new JsonObject { ["open"] = open });
    public static byte[] PairBody(string sender, string? name)
    {
        var o = new JsonObject { ["id"] = sender.Trim() };
        if (name?.Trim() is { Length: > 0 } n) o["name"] = n;
        return Json(o);
    }
    /// <summary>The answer to a knock: <c>do</c> (with the person's own words, when they wrote some), <c>pair</c> or <c>ignore</c>.</summary>
    public static byte[] AnswerBody(string action, string? say = null)
    {
        var o = new JsonObject { ["action"] = action };
        if (say?.Trim() is { Length: > 0 } s) o["say"] = s;
        return Json(o);
    }

    /// <summary>The knocks still waiting on the person.</summary>
    public IEnumerable<Knock> OpenKnocks => Knocks.Where(k => k.State == "open");

    /// <summary>The apps a door can still be added on: one door of each kind a taf.</summary>
    public IEnumerable<DoorApp> Addable => Apps.Where(a => !Doors.Any(d => d.Kind == a.Kind));

    /// <summary>tafs cloud's answer; a door, app or knock it can't read is left out. None for one that isn't an object.</summary>
    public static TafDoors? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        return new(Lenient.Array(a, "doors").Select(ReadDoor).OfType<Door>().ToList(),
            Lenient.Array(a, "apps").Select(App).OfType<DoorApp>().ToList(),
            Lenient.Array(a, "knocks").Select(ReadKnock).OfType<Knock>().ToList());
    }

    /// <summary>One door, as a change answers <c>{"door": …}</c>; none it can't read.</summary>
    public static Door? Changed(JsonElement a) => a.ValueKind == JsonValueKind.Object && Lenient.Field(a, "door") is { } d ? ReadDoor(d) : null;

    static Door? ReadDoor(JsonElement d)
    {
        if (d.ValueKind != JsonValueKind.Object) return null;
        string[] required = ["id", "app", "kind", "label", "address", "proof", "how"];
        if (required.Any(n => Lenient.Text(d, n) is null) || Lenient.Int(d, "today") is not { } today || Lenient.Int(d, "limit") is not { } limit) return null;
        string T(string n) => Lenient.Text(d, n)!;
        var keys = Lenient.Array(d, "keys").Where(k => k.ValueKind == JsonValueKind.Object && Lenient.Text(k, "id") is not null)
            .Select(k => new DoorKey(Lenient.Text(k, "id")!, Lenient.Text(k, "name"), Lenient.Bool(k, "auto"))).ToList();
        var words = Lenient.Field(d, "words") is { ValueKind: JsonValueKind.Object } w && Lenient.Text(w, "state") is { } state
            && Lenient.Text(w, "tap") is { } tap && Lenient.Text(w, "settings") is { } settings ? new DoorWords(state, tap, settings) : null;
        return new(T("id"), T("app"), T("kind"), T("label"), Lenient.Bool(d, "open"), T("address"), T("proof"), T("how"), keys, today, limit,
            Lenient.Bool(d, "resting"), Lenient.Int(d, "others_today"), Lenient.Int(d, "others_limit"), words);
    }

    static DoorApp? App(JsonElement a)
    {
        string[] required = ["id", "label", "kind", "proof", "how"];
        if (a.ValueKind != JsonValueKind.Object || required.Any(n => Lenient.Text(a, n) is null)) return null;
        string T(string n) => Lenient.Text(a, n)!;
        return new(T("id"), T("label"), T("kind"), T("proof"), T("how"));
    }

    static Knock? ReadKnock(JsonElement k)
    {
        if (k.ValueKind != JsonValueKind.Object || Lenient.Text(k, "id") is not { } id || Lenient.Text(k, "door") is not { } door
            || Lenient.Text(k, "app") is not { } app || Lenient.Text(k, "from") is not { } from || Lenient.At(k, "at") is not { } at) return null;
        return new(id, door, app, from, Lenient.Text(k, "name"), Lenient.Text(k, "proof") ?? "none", Lenient.Text(k, "subject"),
            Lenient.Text(k, "text") ?? "", Lenient.Int(k, "attachments") ?? 0, at, Lenient.Text(k, "state") ?? "open", Lenient.Text(k, "rest"),
            Lenient.Int(k, "dropped") ?? 0, Lenient.Bool(k, "paired"));
    }

    /// <summary>Where a door stands, in tafs cloud's words, else the app's: Open, Resting or Closed.</summary>
    public static string State(WordBook w, Door d) =>
        d.Words?.State ?? w.Get(d.Open ? (d.Resting ? "door.status.resting" : "door.status.open") : "door.status.closed");

    /// <summary>Its day: "3 of 20 from you and paired senders today…", or that it had them all; and anyone else's, when it says.</summary>
    public static IReadOnlyList<string> Day(WordBook w, Door d) =>
        new[]
        {
            d.Resting ? w.Count("door.day.resting", d.Limit) : w.Count("door.day.open", d.Limit, ("today", d.Today.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            d.OthersToday is { } others && d.OthersLimit is { } cap
                ? w.Count("door.day.others", cap, ("others", others.ToString(System.Globalization.CultureInfo.InvariantCulture))) : null,
        }.OfType<string>().ToList();

    /// <summary>
    /// A key's name as shown, as the iPad's: "You" only on this person's own key (paired by tafs from their sign-in); a name
    /// that's only the address shows once, as the address.
    /// </summary>
    public static (string Name, string? Under) KeyShown(WordBook w, DoorKey key, string? myEmail)
    {
        if (key.Auto && string.Equals(key.Id, myEmail, StringComparison.OrdinalIgnoreCase)) return (w.Get("door.key.you"), key.Id);
        return key.Name is { Length: > 0 } n && !string.Equals(n, key.Id, StringComparison.OrdinalIgnoreCase) ? (n, key.Id) : (key.Id, null);
    }

    /// <summary>
    /// What a knock says, as the iPad's <c>KnockCard</c>: who wrote (and about what), that nothing was done; a sender check that
    /// failed, or a door that can't tell; attachments not opened. A line says the door had its day, and how many were dropped.
    /// </summary>
    public static IReadOnlyList<string> KnockLines(WordBook w, Knock k)
    {
        if (k.Rest is { } rest) return [w.Count(rest == "others" ? "door.knock.rest_others" : "door.knock.rest_keys", k.Dropped)];
        var lines = new List<string>
        {
            k.Subject is { Length: > 0 } subject ? w.Get("door.knock.wrote_about", ("who", k.Who), ("subject", subject)) : w.Get("door.knock.wrote", ("who", k.Who)),
        };
        if (k.Proof == "fail") lines.Add(w.Get("door.knock.proof_failed", ("from", k.From)));
        else if (k.Proof == "none") lines.Add(w.Get("door.knock.proof_none"));
        if (k.Attachments > 0) lines.Add(w.Count("door.knock.attachments", k.Attachments));
        return lines;
    }
}
