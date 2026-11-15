using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>What ends a taf's Activity, as the iPad's: why the last read failed (with Try again), a read going, its end words, or none.</summary>
public enum ActivityEndKind { None, Error, Loading, More, EndWords, NothingHere }

public sealed record ActivityEnd(ActivityEndKind Kind, string? Words = null);

/// <summary>
/// One filter of a taf's Activity as read from tafs cloud, page after page: the iPad's <c>SourcePages</c> and
/// <c>AppModel.loadActivityPage</c> (TafsCloud+Presentation.swift, AppModel.swift). The first read is the newest page; each
/// more read follows the last page's cursor. A read asked for while one is going waits for it, as <c>moreWhenReady</c>.
/// </summary>
public sealed class ActivityPages
{
    readonly List<ActivityPage> pages = [];
    bool moreWhenReady;
    /// <summary>The cursor whose next page the ring last asked for.</summary>
    string? seenCursor;

    public IReadOnlyList<ActivityPage> Pages => pages;
    public bool Loading { get; private set; }
    public string? Error { get; private set; }

    /// <summary>
    /// <c>GET /v1/tafs/{id}/activity?filter=…</c>, with the app's selection (<c>tools=…&amp;from=…</c>, as tafs cloud gave it)
    /// and the cursor, as the iPad's <c>activityPage</c>.
    /// </summary>
    public static string Path(string taf, string filter, string? cursor, string selection = "") =>
        $"/v1/tafs/{CloudRead.PathPart(taf)}/activity?filter={CloudRead.QueryPart(filter)}"
        + (selection.Length == 0 ? "" : "&" + selection)
        + (cursor is null ? "" : "&cursor=" + CloudRead.QueryPart(cursor));

    /// <summary>
    /// Reads the newest page, or with <paramref name="more"/> the next. A more read that tafs cloud refuses with 409 (its
    /// history moved on) starts again from the newest page; any other failure keeps the pages and says why.
    /// </summary>
    public async Task LoadAsync(string taf, string filter, Func<string, Task<CloudRead>> read, bool more = false, string selection = "")
    {
        if (Loading)
        {
            if (more) moreWhenReady = true;
            return;
        }
        var cursor = more ? pages.LastOrDefault()?.NextCursor : null;
        if (more && cursor is null) return;
        Loading = true;
        moreWhenReady = false;
        Error = null;
        var answer = await read(Path(taf, filter, cursor, selection));
        Loading = false;
        if (answer.Answer is { } json)
        {
            if (!more) pages.Clear();
            pages.Add(TafActivity.Read(json));
            var follow = moreWhenReady;
            moreWhenReady = false;
            if (follow) await LoadAsync(taf, filter, read, more: true, selection);
        }
        else if (more && answer.Status == 409)
        {
            pages.Clear();
            moreWhenReady = false;
            await LoadAsync(taf, filter, read, selection: selection);
        }
        else Error = answer.Why;
    }

    /// <summary>
    /// The next page's ring came into view: its page is read once for each cursor, as the iPad's <c>.task(id: cursor)</c>
    /// on its ProgressView, however often the ring is seen while it's read.
    /// </summary>
    public Task SeenAsync(string taf, string filter, Func<string, Task<CloudRead>> read, string selection = "")
    {
        if (Error is not null || pages.LastOrDefault()?.NextCursor is not { } cursor || cursor == seenCursor) return Task.CompletedTask;
        seenCursor = cursor;
        return LoadAsync(taf, filter, read, more: true, selection);
    }

    /// <summary>
    /// What's under its lines, in the iPad's order: why the last read failed; else, with a cursor, a read of more (made as it
    /// comes into view); else a read going; else tafs cloud's end words; else, with no line at all, "Nothing here.".
    /// </summary>
    public ActivityEnd End(WordBook w) =>
        Error is { } error ? new(ActivityEndKind.Error, error)
        : pages.LastOrDefault()?.NextCursor is not null ? new(ActivityEndKind.More)
        : Loading ? new(ActivityEndKind.Loading)
        : pages.LastOrDefault()?.EndWords is { } words ? new(ActivityEndKind.EndWords, words)
        : pages.All(p => p.Entries == 0) ? new(ActivityEndKind.NothingHere, w.Get("record.nothing_here"))
        : new(ActivityEndKind.None);

    /// <summary>The filters, from the first page, as the iPad draws them.</summary>
    public IReadOnlyList<ActivityFilter> Filters => pages.FirstOrDefault()?.Filters ?? [];

    /// <summary>Every page's days, in order. A next page's day going on from the page before has no title.</summary>
    public IReadOnlyList<ActivityGroup> Groups => pages.SelectMany(p => p.Groups).ToList();
}
