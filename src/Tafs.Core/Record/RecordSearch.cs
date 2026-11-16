using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Record;

/// <summary>A call the search found: a taf's Activity entry, drawn as its Activity draws it, with its taf and what it's about.</summary>
public sealed record RecordHit(ActivityLine Line, string TafId, string TafName, string? About);

/// <summary>One of tafs cloud's groups of hits, under its title (none for the hits it sent without groups).</summary>
public sealed record RecordHitGroup(string Title, IReadOnlyList<RecordHit> Hits);

/// <summary>
/// One page of a search, newest first, as the iPad's <c>TafsCloud.RecordSearchPage</c>: tafs cloud's groups, the box's words
/// (<c>hint</c>), the next page's cursor, and its words for the end and for nothing found.
/// </summary>
public sealed record RecordSearchPage(string Q, string Hint, IReadOnlyList<RecordHitGroup> Groups, string? NextCursor, string? EndWords, string? NoneWords)
{
    public bool IsEmpty => Groups.All(g => g.Hits.Count == 0);
}

/// <summary>One case, as the iPad's <c>TafsCloud.RecordCase</c>: every call about the same thing, oldest first, and the step that didn't go through, in tafs cloud's words.</summary>
public sealed record RecordCase(string About, IReadOnlyList<RecordHit> Steps, string? FailedWords);

/// <summary>
/// The Record searched across the person's tafs (<c>GET /v1/record?q=</c>, kiff/tafs <c>docs/api.md</c>), as the iPad's
/// <c>searchRecord</c> and <c>recordCase</c>: tafs cloud finds, words and decides; the app sends what's typed or tapped.
/// </summary>
public static class RecordSearch
{
    /// <summary>The search for <paramref name="q"/>, its next page by <paramref name="cursor"/>; <paramref name="limit"/> for the box's words alone.</summary>
    public static string Path(string q, string? cursor = null, int? limit = null) =>
        "/v1/record?q=" + CloudRead.QueryPart(q)
        + (cursor is null ? "" : "&cursor=" + CloudRead.QueryPart(cursor))
        + (limit is { } n ? "&limit=" + n.ToString(CultureInfo.InvariantCulture) : "");

    /// <summary>One case: every call about <paramref name="about"/>.</summary>
    public static string CasePath(string about) => "/v1/record/cases/" + CloudRead.PathPart(about);

    /// <summary>A page: tafs cloud's groups, or every entry as one untitled group when it sent none. What's missing is none.</summary>
    public static RecordSearchPage ReadPage(JsonElement a)
    {
        var groups = Lenient.Array(a, "groups")
            .Select(g => new RecordHitGroup(Lenient.Text(g, "title") ?? "", Hits(g, "entries")))
            .ToList();
        if (groups.Count == 0 && Hits(a, "entries") is { Count: > 0 } all) groups = [new RecordHitGroup("", all)];
        return new(Lenient.Text(a, "q") ?? "", Lenient.Text(a, "hint")?.Trim() ?? "", groups,
            Lenient.Text(a, "next_cursor"), Words(a, "end_words"), Words(a, "none_words"));
    }

    /// <summary>A case, its steps oldest first, and the failed step's words; none without what it's about.</summary>
    public static RecordCase? ReadCase(JsonElement a) =>
        Lenient.Text(a, "about") is { } about
            ? new(about, Hits(a, "steps"), Lenient.Field(a, "failed") is { } f ? Words(f, "words") : null)
            : null;

    /// <summary>Each hit an Activity entry with its taf; one with no taf id, or no id or title, is left out.</summary>
    static List<RecordHit> Hits(JsonElement o, string name) =>
        Lenient.Array(o, name).Select(e =>
        {
            var taf = Lenient.Field(e, "taf") ?? default;
            return TafActivity.Line(e) is { } line && Lenient.Text(taf, "id") is { Length: > 0 } id
                ? new RecordHit(line, id, Lenient.Text(taf, "name") ?? "", Words(e, "about"))
                : null;
        }).OfType<RecordHit>().ToList();

    static string? Words(JsonElement o, string name) => Lenient.Text(o, name) is { } s && s.Trim().Length > 0 ? s : null;
}
