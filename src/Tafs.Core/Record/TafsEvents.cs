using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// Something that happened in tafs itself, as tafs cloud's <c>GET /v1/events</c> keeps it (the iPad's
/// <c>TafsCloud.AccountEvent</c>): a halt or a resume, a knock and what became of it, someone paired, a door removed. Its
/// taf by tafs cloud's id, none when it was all of them.
/// </summary>
public sealed record TafsEvent(DateTimeOffset At, string Kind, string? Taf = null, string? App = null, string? From = null);

/// <summary>
/// tafs' own events in the Record, beside KIFF's calls (the iPad's <c>inTafs</c> and <c>eventWords</c>, kiff/tafs#236 items
/// 15 and 19): one line a taf, or one for each taf when it was all of them, dim, with no receipt (they aren't calls).
/// </summary>
public static class TafsEvents
{
    public const string Path = "/v1/events";

    /// <summary>tafs cloud's events; one without a time or a kind is left out. None for an answer that isn't a list.</summary>
    public static IReadOnlyList<TafsEvent>? Read(JsonElement answer) =>
        Lenient.Field(answer, "events") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object && Lenient.Text(e, "kind") is { Length: > 0 } && Lenient.Text(e, "at") is { } at
                    && KiffActivity.Parse(at) is not null)
                .Select(e => new TafsEvent(KiffActivity.Parse(Lenient.Text(e, "at")!)!.Value, Lenient.Text(e, "kind")!, Lenient.Text(e, "taf"),
                    Lenient.Text(e, "app"), Lenient.Text(e, "from")))
                .ToList()
            : null;

    /// <summary>
    /// An event's title and the words under it, as the iPad's <c>eventWords</c>; none for a kind it doesn't know, which isn't
    /// drawn. The door by its app's name ("a door" without one), who by tafs cloud's word for them ("Someone" without one).
    /// </summary>
    public static (string Title, string Detail)? Words(WordBook w, TafsEvent e)
    {
        var door = e.App is { Length: > 0 } app ? Coach.ChatDoor.Label(app) : w.Get("record.event.a_door");
        var who = e.From is { Length: > 0 } from ? from : w.Get("record.event.someone");
        return e.Kind switch
        {
            "halted" => (w.Get(e.Taf is null ? "record.event.halted_all" : "record.event.halted"), w.Get("record.event.halted_detail")),
            "resumed" => (w.Get(e.Taf is null ? "record.event.resumed_all" : "record.event.resumed"), w.Get("record.event.resumed_detail")),
            "knocked" => (w.Get("record.event.knocked", ("who", who), ("door", door)), w.Get("record.event.knocked_detail")),
            "knock_done" => (w.Get("record.event.knock_done", ("who", who)), w.Get("record.event.knock_done_detail", ("door", door))),
            "knock_ignored" => (w.Get("record.event.knock_ignored", ("who", who)), w.Get("record.event.knock_ignored_detail", ("door", door))),
            "paired" => (w.Get("record.event.paired", ("who", who)), w.Get("record.event.paired_detail", ("door", door))),
            "door_removed" => (w.Get("record.event.door_removed", ("door", door)), w.Get("record.event.door_removed_detail")),
            _ => null,
        };
    }

    /// <summary>
    /// The events as Record lines: one for its taf, or one for each of <paramref name="tafs"/> when it was all of them; an
    /// event of a taf that isn't listed, or of a kind the app doesn't know, is left out.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Lines(WordBook w, IEnumerable<TafsEvent> events,
        IReadOnlyList<(string Id, string Name, int Colour)> tafs)
    {
        var lines = new List<RecordEntry>();
        foreach (var e in events)
        {
            if (Words(w, e) is not { } said) continue;
            foreach (var taf in e.Taf is { } one ? tafs.Where(t => t.Id == one) : tafs)
                lines.Add(new RecordEntry(taf.Name, taf.Colour, EntryKind.InTafs, said.Title, said.Detail, e.At));
        }
        return lines;
    }

    /// <summary>KIFF's calls and tafs' events together, newest first, as the iPad sorts its Record.</summary>
    public static IReadOnlyList<RecordEntry> With(IReadOnlyList<RecordEntry> calls, IReadOnlyList<RecordEntry> events) =>
        [.. calls.Concat(events).OrderByDescending(e => e.At)];
}
