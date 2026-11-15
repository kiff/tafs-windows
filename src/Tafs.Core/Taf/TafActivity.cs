using System.Globalization;
using System.Text.Json;
using Tafs.Core.Record;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One line of a taf's Activity: what became of a call (its dot), what it was, the words under it, and its time.</summary>
public sealed record ActivityLine(string Id, EntryKind Kind, string Title, string Detail, string Time, string? Amount = null);

/// <summary>One day of a taf's Activity: its eyebrow and its lines, newest first.</summary>
public sealed record ActivityGroup(string Title, IReadOnlyList<ActivityLine> Lines);

/// <summary>A filter of a taf's Activity: its id, sent back to tafs cloud, and its title.</summary>
public sealed record ActivityFilter(string Id, string Title);

/// <summary>
/// A taf's Activity, as tafs cloud's <c>GET /v1/tafs/{id}/activity</c> answers it (contracts/taf-activity.json): its days,
/// the filters with the one applied, the words under the last line, and the revision sent back as If-None-Match.
/// </summary>
public sealed record ActivityPage(IReadOnlyList<ActivityGroup> Groups, IReadOnlyList<ActivityFilter> Filters, string Filter,
    string? EndWords, string? Revision);

/// <summary>A taf's Activity tab: read from tafs cloud, or the iPad's sample (the reference set's taf.png).</summary>
public static class TafActivity
{
    /// <summary>
    /// The answer's days and filters, in tafs cloud's words and order. A kind this app doesn't know reads as Unknown, so it
    /// waits under amber rather than passing as done; an entry without an id or a title is left out.
    /// </summary>
    public static ActivityPage Read(JsonElement answer)
    {
        var groups = Lenient.Array(answer, "groups")
            .Select(g => new ActivityGroup(Lenient.Text(g, "title") ?? "", Lenient.Array(g, "entries").Select(Line).OfType<ActivityLine>().ToList()))
            .Where(g => g.Lines.Count > 0)
            .ToList();
        var filters = Lenient.Array(answer, "filters")
            .Select(f => Lenient.Text(f, "id") is { } id && Lenient.Text(f, "title") is { } title ? new ActivityFilter(id, title) : null)
            .OfType<ActivityFilter>()
            .ToList();
        return new ActivityPage(groups, filters, Lenient.Text(answer, "filter") ?? "all", Lenient.Text(answer, "end_words"), Lenient.Text(answer, "revision"));
    }

    static ActivityLine? Line(JsonElement e) =>
        Lenient.Text(e, "id") is { } id && Lenient.Text(e, "title") is { } title
            ? new ActivityLine(id, EntryKinds.Parse(Lenient.Text(e, "kind")), title, Lenient.Text(e, "detail") ?? "", Lenient.Text(e, "time") ?? "", Lenient.Text(e, "amount_text"))
            : null;

    /// <summary>The iPad's filters of a taf's Activity (its ActivityFilter): All, Done (its own word, not the Record's), Held, Refused.</summary>
    public static IReadOnlyList<ActivityFilter> Filters(WordBook w) =>
    [
        new("all", w.Get("record.filter.all")),
        new("done", w.Get("taf.halt.part_held")),
        new("held", w.Get("record.filter.held")),
        new("refused", w.Get("record.filter.refused")),
    ];

    /// <summary>The iPad's rules: Done keeps done and approved; Held, held; Refused, refused, not done and revoked. All keeps every line, its start too.</summary>
    public static bool Keeps(string filter, EntryKind kind) => filter switch
    {
        "done" => kind is EntryKind.Done or EntryKind.Approved,
        "held" => kind is EntryKind.Held,
        "refused" => kind is EntryKind.Refused or EntryKind.NotDone or EntryKind.Revoked,
        _ => true,
    };

    /// <summary>With nothing to show: "Nothing yet." under All, "Nothing here." once narrowed.</summary>
    public static string Empty(WordBook w, string filter) => w.Get(filter == "all" ? "common.nothing_yet" : "record.nothing_here");

    /// <summary>
    /// The sample's Activity for one of its tafs: that taf's lines of the sample Record, narrowed by the filter, by day in
    /// <paramref name="zone"/>, newest first ("TODAY · NOV 15"; 24-hour times, as the iPad shows them).
    /// </summary>
    public static ActivityPage Sample(WordBook w, string taf, string filter, DateTimeOffset now, TimeZoneInfo zone)
    {
        var entries = RecordScreen.Sample(w, now).Where(e => e.Taf == taf && Keeps(filter, e.Kind));
        var days = RecordScreen.Days(w, entries, RecordFilter.All, taf, now, zone);
        var groups = days.Select(d => new ActivityGroup(d.Title, d.Entries.Select((e, i) => new ActivityLine(
            $"{d.Title}-{i}", e.Kind, e.Title, e.Detail,
            TimeZoneInfo.ConvertTime(e.At, zone).ToString("HH:mm", CultureInfo.InvariantCulture))).ToList())).ToList();
        return new ActivityPage(groups, Filters(w), filter, null, null);
    }
}
