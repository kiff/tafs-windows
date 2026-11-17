using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>An app event's approval at planque: <c>needed</c> until the owner's phone or PC approves it, <c>approved</c> after.</summary>
public sealed record KnobApproval(string State, string Grant, string Event, string? Subscription);

/// <summary>One choice of a stop's size, and the whole <c>PUT …/knob</c> body that makes it, kept as tafs cloud wrote it.</summary>
public sealed record KnobChoice(string Label, JsonElement Setting);

/// <summary>A stop's − and +: its label, the choice it's at, and the choices tafs cloud offers.</summary>
public sealed record KnobSize(string Label, int Index, IReadOnlyList<KnobChoice> Choices)
{
    /// <summary>The choice one step down (−1) or up (+1), as the iPad's <c>choice(moving:)</c>; none past either end.</summary>
    public KnobChoice? Moving(int direction) =>
        direction is -1 or 1 && Index >= 0 && Index < Choices.Count && Index + direction >= 0 && Index + direction < Choices.Count
            ? Choices[Index + direction] : null;

    /// <summary>The label with the choice it's at ("How often · Every hour"), as the iPad's stepper reads it.</summary>
    public string Shown => Index >= 0 && Index < Choices.Count ? $"{Label} · {Choices[Index].Label}" : Label;
}

/// <summary>
/// One stop of the dial: a trigger on When (<c>wakes</c>), a place on Where (<c>results</c>). Every label and line is tafs
/// cloud's, in the account's language and zone; a stop that doesn't say whether it's on is on.
/// </summary>
public sealed record KnobItem(string Id, string? Kind, string? Place, string Label, string Words, bool On, DateTimeOffset? Expires,
    string? Ok, string? FollowUp, string? Grant, KnobApproval? Approval, KnobSize? Size, JsonElement? Trigger)
{
    /// <summary>Its line, else its label: what a schedule or a follow-up says.</summary>
    public string Line => Words.Length > 0 ? Words : Label;
}

/// <summary>The version Make it live switches to, and how many stops it changes.</summary>
public sealed record KnobPending(int Version, int Changes);

/// <summary>What a change to the dial made: the taf's next version, and what it changes in words. Nothing runs from it until it's live.</summary>
public sealed record KnobChange(int Version, IReadOnlyList<string> Words)
{
    /// <summary>tafs cloud's answer to <c>PUT …/knob</c>; none it can't read.</summary>
    public static KnobChange? Read(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Int(a, "version") is { } v
            ? new(v, Lenient.Array(a, "words").Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList()) : null;
}

/// <summary>
/// A taf's dial as tafs cloud sends it (<c>GET /v1/tafs/{id}/knob</c>, kiff/tafs <c>docs/api.md</c>), as the iPad's
/// <c>TafsCloud.Knob</c>: what Setup's Starts when and Results go to read. A change to it waits as the taf's next version
/// (<see cref="Pending"/>, its words in <see cref="DraftWords"/>) until it's made live.
/// </summary>
public sealed record TafKnob(bool CanPause, int? Version, KnobPending? Pending, IReadOnlyList<KnobItem> Wakes, IReadOnlyList<KnobItem> Results,
    IReadOnlyList<string> DraftWords, string? Busy, IReadOnlyList<string>? SettingPlaces, JsonElement? Setting = null, int? Shown = null,
    int? DraftVersion = null)
{
    /// <summary>
    /// Whether a choice may be made now, as the iPad's <c>acceptsChoices(waitingFor:)</c>: only from the newest draft this PC
    /// knows. A change made here and not yet read back (<paramref name="waitingFor"/>) is newer than an older read's.
    /// </summary>
    public bool AcceptsChoices(int? waitingFor = null)
    {
        var pending = Math.Max(Pending?.Version ?? DraftVersion ?? 0, waitingFor ?? 0);
        return pending == 0 || pending <= (Version ?? 0) || Shown == pending;
    }

    /// <summary>
    /// A setting as <c>PUT …/knob</c> takes it, as the iPad's <c>Setting.encode</c>: its triggers as tafs cloud wrote them,
    /// <c>when: {busy}</c>, where results go, and what and why when it has them. A change copies the live one, changes one thing
    /// and sends it whole.
    /// </summary>
    public static byte[] SettingBody(JsonElement setting, string? busy = null, Func<JsonElement, bool>? keepWake = null)
    {
        static JsonNode? Copy(JsonElement e) => JsonNode.Parse(e.GetRawText());
        var o = new JsonObject
        {
            ["wakes"] = new JsonArray(Lenient.Array(setting, "wakes").Where(x => keepWake?.Invoke(x) != false).Select(Copy).ToArray()),
            ["when"] = new JsonObject { ["busy"] = busy ?? BusyOf(setting) },
            ["results"] = new JsonArray(Lenient.Array(setting, "results").Select(Copy).ToArray()),
        };
        if (Lenient.Field(setting, "what") is { ValueKind: JsonValueKind.Array } what) o["what"] = Copy(what);
        if (Lenient.Text(setting, "why") is { } why) o["why"] = why;
        return Encoding.UTF8.GetBytes(o.ToJsonString());
    }

    static string BusyOf(JsonElement s) =>
        Lenient.Text(s, "busy") ?? (Lenient.Field(s, "when") is { ValueKind: JsonValueKind.Object } when ? Lenient.Text(when, "busy") : null) ?? "skip";

    /// <summary>What a busy taf does, changed (<c>skip</c> or <c>latest</c>); none when that's what it does already.</summary>
    public byte[]? WithBusy(string busy) => Setting is { } s && BusyOf(s) != busy ? SettingBody(s, busy) : null;

    /// <summary>The live setting without one trigger: Stop on an app event it listens to.</summary>
    public byte[]? WithoutWake(JsonElement trigger) =>
        Setting is { } s && Lenient.Array(s, "wakes").Any(x => Same(x, trigger)) ? SettingBody(s, keepWake: x => !Same(x, trigger)) : null;

    /// <summary>The same JSON, whatever its spacing or the order of its fields.</summary>
    static bool Same(JsonElement a, JsonElement b) => JsonNode.DeepEquals(JsonNode.Parse(a.GetRawText()), JsonNode.Parse(b.GetRawText()));

    public static string Path(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/knob";

    /// <summary><c>DELETE</c>: a follow-up the taf set itself, by the id its dial gives.</summary>
    public static string FollowUpPath(string taf, string followUp) =>
        "/v1/tafs/" + CloudRead.PathPart(taf) + "/follow-ups/" + CloudRead.PathPart(followUp);

    /// <summary><c>POST</c>: every start off (<c>pause</c>) or on again (<c>resume</c>), its own schedules, the dial's and its follow-ups.</summary>
    public static string PausePath(string taf, bool pause) => "/v1/tafs/" + CloudRead.PathPart(taf) + (pause ? "/pause" : "/resume");

    /// <summary><c>PUT</c>: where tafs delivers its results itself, live at once.</summary>
    public static string ResultsPath(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/results";

    public IEnumerable<KnobItem> Schedules => Wakes.Where(i => i.Kind == "schedule");
    public IEnumerable<KnobItem> AppEvents => Wakes.Where(i => i.Kind == "app_event");
    public IEnumerable<KnobItem> FollowUps => Wakes.Where(i => i.Kind == "follow_up");

    /// <summary>The places in the person's connected apps: every place but tafs' own.</summary>
    public IEnumerable<KnobItem> AppPlaces => Results.Where(i => !ResultPlaces.Own.Contains(i.Place ?? ""));

    /// <summary>
    /// The dial, read as strictly as the iPad's decoder: every stop has its id, and each field present has its type; one it
    /// can't read is no dial ("Reading what starts it…" stays). Fields tafs cloud adds later are no matter.
    /// </summary>
    public static TafKnob? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var wakes = Items(a, "wakes");
            var results = Items(a, "results");
            if (wakes is null || results is null) return null;
            var pending = Lenient.Field(a, "pending") is { ValueKind: JsonValueKind.Object } p
                ? new KnobPending(p.GetProperty("version").GetInt32(), Lenient.Int(p, "changes") ?? 0) : null;
            var draft = Lenient.Field(a, "draft") is { ValueKind: JsonValueKind.Object } d ? Texts(d, "words") : [];
            string? busy = null;
            IReadOnlyList<string>? places = null;
            if (Lenient.Field(a, "setting") is { ValueKind: JsonValueKind.Object } s)
            {
                busy = Lenient.Text(s, "busy") ?? (Lenient.Field(s, "when") is { ValueKind: JsonValueKind.Object } when ? Lenient.Text(when, "busy") : null) ?? "skip";
                places = Lenient.Array(s, "results").Select(ResultPlaces.Of).OfType<string>().ToList();
            }
            return new(Lenient.Field(a, "can_pause") is not { ValueKind: JsonValueKind.False }, Lenient.Int(a, "version"), pending, wakes, results,
                draft, busy, places, Lenient.Field(a, "setting") is { ValueKind: JsonValueKind.Object } setting ? setting.Clone() : null,
                Lenient.Int(a, "shown"), Lenient.Field(a, "draft") is { ValueKind: JsonValueKind.Object } dv ? Lenient.Int(dv, "version") : null);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    static IReadOnlyList<string> Texts(JsonElement o, string name) =>
        Lenient.Array(o, name).Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList();

    /// <summary>A ring's stops; none when it isn't there, no dial when a stop has no id or a field the wrong type.</summary>
    static IReadOnlyList<KnobItem>? Items(JsonElement a, string ring)
    {
        if (Lenient.Field(a, ring) is not { } list) return [];
        if (list.ValueKind != JsonValueKind.Array) return null;
        var items = new List<KnobItem>();
        foreach (var i in list.EnumerateArray())
        {
            if (i.ValueKind != JsonValueKind.Object || Lenient.Text(i, "id") is not { } id) return null;
            string[] texts = ["kind", "place", "label", "words", "state", "tap", "expires", "door", "grant", "action", "ok", "look", "follow_up"];
            if (texts.Any(t => Lenient.Field(i, t) is { } v && v.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))) return null;
            if (Lenient.Field(i, "on") is { } on && on.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
            items.Add(new KnobItem(id, Lenient.Text(i, "kind"), Lenient.Text(i, "place"), Lenient.Text(i, "label") ?? "", Lenient.Text(i, "words") ?? "",
                Lenient.Field(i, "on") is not { ValueKind: JsonValueKind.False }, Lenient.At(i, "expires"), Lenient.Text(i, "ok"),
                Lenient.Text(i, "follow_up"), Lenient.Text(i, "grant"), Approval(i), Size(i),
                Lenient.Field(i, "trigger") is { ValueKind: not JsonValueKind.Null } t ? t.Clone() : null));
        }
        return items;
    }

    static KnobApproval? Approval(JsonElement i) =>
        Lenient.Field(i, "approval") is { ValueKind: JsonValueKind.Object } a
            ? new KnobApproval(a.GetProperty("state").GetString()!, a.GetProperty("grant").GetString()!, a.GetProperty("event").GetString()!,
                Lenient.Text(a, "subscription"))
            : null;

    static KnobSize? Size(JsonElement i) =>
        Lenient.Field(i, "size") is { ValueKind: JsonValueKind.Object } s
            ? new KnobSize(s.GetProperty("label").GetString()!, s.GetProperty("index").GetInt32(),
                s.GetProperty("choices").EnumerateArray().Select(c => new KnobChoice(c.GetProperty("label").GetString()!, c.GetProperty("setting").Clone())).ToList())
            : null;
}

/// <summary>A place tafs delivers to itself: its id, its title and its line, in the same words on every platform.</summary>
public sealed record OwnPlace(string Place, string Title, string Line, bool On, bool CanSwitch);

/// <summary>
/// Setup › Results go to, as the iPad's <c>ResultsGoToView</c>: tafs' own places (its door's reply, the app, the owner's
/// email) are set at once (<c>PUT …/results</c>) from what the live taf has; at least one stays on, so its last switch is off.
/// </summary>
public static class ResultPlaces
{
    /// <summary>tafs' own places, in this order.</summary>
    public static readonly string[] Own = ["door_reply", "app", "owner_email"];

    /// <summary>A place by name (<c>"app"</c>), or with what it needs (<c>{place, …}</c>).</summary>
    public static string? Of(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Object => Lenient.Text(v, "place"),
        _ => null,
    };

    /// <summary>
    /// tafs' own places on, as the live taf has them: its <c>results</c> as tafs cloud last sent them, never a waiting
    /// version's. Without them, the dial's when it shows the live version (nothing pending); none when they aren't known.
    /// </summary>
    public static IReadOnlyList<string>? Live(IReadOnlyList<string>? live, TafKnob? knob)
    {
        if (live is not null) return Own.Where(live.Contains).ToList();
        if (knob is null || knob.Pending is not null) return null;
        return Own.Where(p => knob.Results.FirstOrDefault(r => r.Place == p)?.On ?? knob.SettingPlaces?.Contains(p) ?? false).ToList();
    }

    /// <summary>What <c>PUT …/results</c> is sent when one is switched: the live ones with that one changed; none when it'd leave none.</summary>
    public static IReadOnlyList<string>? Switched(IReadOnlyList<string> live, string place, bool on)
    {
        var places = live.Where(p => p != place).ToList();
        if (on) places.Add(place);
        var sent = Own.Where(places.Contains).ToList();
        return sent.Count == 0 ? null : sent;
    }

    /// <summary>
    /// tafs' own places as the screen shows them: each on as the live taf has it; switchable for whoever manages the taf,
    /// once the live places are known, and never the last one on.
    /// </summary>
    public static IReadOnlyList<OwnPlace> Shown(WordBook w, IReadOnlyList<string>? live, string? email, bool manages, bool busy)
    {
        OwnPlace Row(string place, string title, string line)
        {
            var on = live?.Contains(place) ?? false;
            return new(place, title, line, on, manages && !busy && live is not null && !(on && live.Count <= 1));
        }
        return
        [
            Row("door_reply", w.Get("setup.place.reply_title"), w.Get("setup.place.reply_words")),
            Row("app", w.Get("setup.place.app_title"), w.Get("setup.place.app_words")),
            Row("owner_email", w.Get("setup.place.email_title"),
                email is { Length: > 0 } ? w.Get("setup.place.email_words", ("email", email)) : w.Get("setup.place.email_words_yours")),
        ];
    }

    /// <summary>The live taf's places as <c>PUT …/results</c> answers them (<c>{taf: {results}}</c>); none when it doesn't say.</summary>
    public static IReadOnlyList<string>? Answered(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && Lenient.Field(answer, "taf") is { ValueKind: JsonValueKind.Object } taf
            && Lenient.Field(taf, "results") is { ValueKind: JsonValueKind.Array } results
            ? results.EnumerateArray().Select(Of).OfType<string>().ToList()
            : null;

    /// <summary><c>{"places": [...]}</c>, as <c>PUT …/results</c> takes it.</summary>
    public static byte[] Body(IReadOnlyList<string> places) => JsonSerializer.SerializeToUtf8Bytes(new { places });
}
