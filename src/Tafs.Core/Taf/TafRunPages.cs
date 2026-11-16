using System.Text.Json;

namespace Tafs.Core.Taf;

/// <summary>
/// A page of the runs a taf kept (<c>GET /v1/tafs/{id}/work/runs?limit=&amp;cursor=</c>), as the iPad's <c>RetainedRuns</c>: tafs
/// cloud's title for them, its "see them all" words, the runs (as its work gives them) and, on the first page, the firings
/// that started no run; the next page's cursor, and tafs cloud's words at the end of what's kept.
/// </summary>
public sealed record RunPage(string? Title, string? MoreTitle, string? EndWords, string? NextCursor, IReadOnlyList<WorkRequest> Requests,
    IReadOnlyList<WorkSkipped> Skipped)
{
    /// <summary>The panel's preview reads 3; seeing them all reads 20 a page.</summary>
    public static string Path(string taf, int limit, string? cursor = null) =>
        $"/v1/tafs/{CloudRead.PathPart(taf)}/work/runs?limit={limit}" + (cursor is null ? "" : "&cursor=" + CloudRead.QueryPart(cursor));

    public static RunPage? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        return new(Lenient.Text(a, "title"), Lenient.Text(a, "more_title"), Lenient.Text(a, "end_words"), Lenient.Text(a, "next_cursor"),
            Lenient.Array(a, "requests").Select(TafWork.Request).OfType<WorkRequest>().ToList(), TafWork.ReadSkipped(a));
    }

    /// <summary>The runs and, on the first page, the firings that started no run, each in its place by time, newest first.</summary>
    public IReadOnlyList<TafWorkRow> Rows(bool first) =>
        Requests.Select(r => new TafWorkRow(r.At, r, null)).Concat(first ? Skipped.Select(s => new TafWorkRow(s.Due, null, s)) : [])
            .OrderByDescending(r => r.At).ToList();
}

/// <summary>One step of a run, as tafs cloud says it: done, failed, not made, waiting, or next; one waiting on an app's event, the app's.</summary>
public sealed record RunStep(string Label, string? Line, string State, bool WaitsOnApp);

/// <summary>
/// A run's own page (<c>GET /v1/tafs/{id}/requests/{run}</c>), as the iPad's <c>KnobRequestDetail</c> (S19): where it came from
/// ("By email", "Run now"), its line, each step, and where its reply went, with Try the reply now for whoever manages the taf.
/// </summary>
public sealed record RunDetail(string From, string Line, IReadOnlyList<RunStep> Steps, IReadOnlyList<WorkDelivery> Deliveries)
{
    public static string Path(string taf, string run) => $"/v1/tafs/{CloudRead.PathPart(taf)}/requests/{CloudRead.PathPart(run)}";

    public static RunDetail? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var steps = Lenient.Array(a, "steps").Where(s => s.ValueKind == JsonValueKind.Object && Lenient.Text(s, "label") is not null)
            .Select(s => new RunStep(Lenient.Text(s, "label")!, Lenient.Text(s, "line"), Lenient.Text(s, "state") ?? "", Lenient.Text(s, "waits_on") == "app")).ToList();
        var deliveries = Lenient.Array(a, "deliveries").Where(d => d.ValueKind == JsonValueKind.Object).Select(TafWork.Delivery).ToList();
        return new(Lenient.Text(a, "from") ?? "", Lenient.Text(a, "line") ?? "", steps, deliveries);
    }
}
